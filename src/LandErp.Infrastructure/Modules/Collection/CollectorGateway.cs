using LandErp.Application.Foundation;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.Collection.Contracts;
using LandErp.Application.Modules.Collection.Domain;
using LandErp.Collector.Contracts.V1;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LandErp.Infrastructure.Modules.Collection;

/// <summary>Short database transactions serialize each agent and fence expired work. No browser dependencies.</summary>
public sealed class CollectorGateway(IDbContextFactory<LandErpDbContext> factory, TimeProvider time) : ICollectorGateway
{
    public async Task RegisterAsync(AgentCredential credential, AgentRegistration registration, CancellationToken cancellationToken)
    {
        if (registration.ContractVersion != 1 || string.IsNullOrWhiteSpace(registration.Version) || registration.Version.Length > 32
            || registration.Capabilities == null || registration.Capabilities.Length is < 1 or > 2
            || registration.Capabilities.Any(value => !Enum.IsDefined(value)))
            throw new CollectorProtocolException("VERSION_OR_CAPABILITY_UNSUPPORTED");
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        CollectorAgent agent = await AuthenticateAsync(db, credential, cancellationToken);
        agent.VersionText = registration.Version;
        agent.Capabilities = string.Join(',', registration.Capabilities.Distinct().Order());
        agent.RegisteredAt ??= time.GetUtcNow(); agent.LastHeartbeatAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
    }

    public async Task HeartbeatAsync(AgentCredential credential, AgentHeartbeat heartbeat, CancellationToken cancellationToken)
    {
        if (heartbeat.SourceStatus != null && !Enum.IsDefined(heartbeat.SourceStatus.Value)) throw new ArgumentException("SOURCE_STATUS_INVALID");
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        CollectorAgent agent = await AuthenticateAsync(db, credential, cancellationToken);
        agent.LastHeartbeatAt = time.GetUtcNow();
        if (heartbeat.JobId != null || heartbeat.LeaseId != null)
        {
            ServerCollectionJob job = await LockedJobAsync(db, heartbeat.JobId ?? Guid.Empty, agent.Id, cancellationToken);
            EnsureLease(job, heartbeat.LeaseId ?? Guid.Empty);
            job.LeaseExpiresAt = time.GetUtcNow().AddMinutes(3);
            if (heartbeat.SourceStatus != null) job.ResultCode = heartbeat.SourceStatus.Value.ToString();
        }
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
    }

    public async Task<CollectionWork?> ClaimAsync(AgentCredential credential, CancellationToken cancellationToken)
    {
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        CollectorAgent agent = await AuthenticateAsync(db, credential, cancellationToken);
        if (agent.RegisteredAt == null) throw new CollectorProtocolException("REGISTRATION_REQUIRED");
        DateTimeOffset now = time.GetUtcNow();
        string[] capabilities = agent.Capabilities.Split(',', StringSplitOptions.RemoveEmptyEntries);
        // Shared work is assigned only here. SKIP LOCKED prevents two compatible agents leasing the same job.
        var jobs = await db.CollectionJobs.FromSqlInterpolated($"""
            SELECT j.* FROM collection.jobs j JOIN collection.search_configurations s ON s.id=j.search_id
            WHERE j.organization_id={agent.OrganizationId} AND s.enabled=true AND s.source=ANY({capabilities})
              AND (j.state='Pending' OR (j.state='Leased' AND j.lease_expires_at<={now}))
            ORDER BY j.created_at LIMIT 1 FOR UPDATE OF j SKIP LOCKED
            """).ToListAsync(cancellationToken);
        if (jobs.Count == 0) { await transaction.CommitAsync(cancellationToken); return null; }
        ServerCollectionJob job = jobs[0];
        SearchConfiguration search = await db.SearchConfigurations.SingleAsync(item => item.Id == job.SearchId, cancellationToken);
        job.AgentId = agent.Id; job.State = CollectionJobState.Leased;
        job.LeaseId = DataConventions.NewId(); job.LeaseExpiresAt = now.AddMinutes(3);
        agent.LastHeartbeatAt = now;
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        return new(job.Id, job.LeaseId.Value, job.LeaseExpiresAt.Value, CollectorSource(search.Source), search.Url, search.MaxPages, search.Label);
    }

    public async Task<CollectionReceipt> AcceptAsync(AgentCredential credential, CollectionResult result, CancellationToken cancellationToken)
    {
        if (result.ResultId == Guid.Empty || result.Observations == null || result.Observations.Length > 25 || !Enum.IsDefined(result.Outcome)
            || !result.Final && result.Outcome != CollectionOutcome.Success)
            throw new ArgumentException("COLLECTION_RESULT_INVALID");
        foreach (ObservationEnvelope envelope in result.Observations)
        {
            if (envelope == null || string.IsNullOrWhiteSpace(envelope.ObservationKey) || envelope.ObservationKey.Length > 128)
                throw new ArgumentException("OBSERVATION_KEY_INVALID");
            ContractRules.Validate(envelope.Data);
        }
        string payloadHash = Hash(JsonSerializer.Serialize(result, CollectionJson.Options));
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        CollectorAgent agent = await AuthenticateAsync(db, credential, cancellationToken);
        CollectionDelivery? delivered = await db.CollectionDeliveries.SingleOrDefaultAsync(item => item.Id == result.ResultId, cancellationToken);
        if (delivered != null)
        {
            if (delivered.AgentId != agent.Id || delivered.JobId != result.JobId || delivered.PayloadHash != payloadHash)
                throw new CollectorProtocolException("IDEMPOTENCY_CONFLICT");
            return JsonSerializer.Deserialize<CollectionReceipt>(delivered.ReceiptJson, CollectionJson.Options)!;
        }
        ServerCollectionJob job = await LockedJobAsync(db, result.JobId, agent.Id, cancellationToken);
        EnsureLease(job, result.LeaseId);
        SearchConfiguration search = await db.SearchConfigurations.SingleAsync(item => item.Id == job.SearchId, cancellationToken);
        if (result.Observations.Any(item => MapSource(item.Data.Source) != search.Source)) throw new ArgumentException("SOURCE_MISMATCH");
        // Global ordering of natural identity locks avoids two overlapping search batches deadlocking.
        foreach (string identity in result.Observations.Select(item => $"{agent.OrganizationId}:{item.Data.Source}:{item.Data.ExternalId}").Distinct().Order(StringComparer.Ordinal))
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({identity},0))", cancellationToken);
        int accepted = 0; int duplicates = 0;
        foreach (ObservationEnvelope envelope in result.Observations)
        {
            ListingData data = envelope.Data;
            string json = JsonSerializer.Serialize(data, CollectionJson.Options); string hash = Hash(json);
            CatalogObservation? keyed = db.ListingObservations.Local.FirstOrDefault(item => item.AgentId == agent.Id && item.ObservationKey == envelope.ObservationKey)
                ?? await db.ListingObservations.SingleOrDefaultAsync(item => item.AgentId == agent.Id && item.ObservationKey == envelope.ObservationKey, cancellationToken);
            if (keyed != null)
            {
                if (keyed.ContentHash != hash) throw new CollectorProtocolException("OBSERVATION_KEY_CONFLICT");
                duplicates++; continue;
            }
            CatalogSource source = MapSource(data.Source);
            Listing? listing = db.Listings.Local.FirstOrDefault(item => item.OrganizationId == agent.OrganizationId && item.Source == source && item.ExternalId == data.ExternalId)
                ?? await db.Listings.SingleOrDefaultAsync(item => item.OrganizationId == agent.OrganizationId && item.Source == source && item.ExternalId == data.ExternalId, cancellationToken);
            bool isNew = listing == null;
            listing ??= new()
            {
                Id = DataConventions.NewId(),
                OrganizationId = agent.OrganizationId,
                Source = source,
                ExternalId = data.ExternalId,
                Url = data.Url,
                FirstObservedAt = data.ObservedAt,
                LastObservedAt = data.ObservedAt,
                IngestionKind = CatalogIngestionKind.Collector,
                Provenance = "Collector V1",
                ReceivedAt = time.GetUtcNow(),
                RecordedAt = time.GetUtcNow(),
                ChangedAt = time.GetUtcNow()
            };
            if (await db.ListingObservations.AnyAsync(item => item.ListingId == listing.Id && item.ObservedAt == data.ObservedAt && item.ContentHash == hash, cancellationToken)
                || db.ListingObservations.Local.Any(item => item.ListingId == listing.Id && item.ObservedAt == data.ObservedAt && item.ContentHash == hash))
            { duplicates++; continue; }
            List<string> changes = [];
            if (isNew || data.ObservedAt > listing.LastObservedAt)
            {
                ApplyKnown(listing, data, changes);
                listing.LastObservedAt = data.ObservedAt;
                if (changes.Count > 0 && !isNew)
                {
                    listing.DataRevision++; listing.ChangedAt = time.GetUtcNow();
                    listing.QueueReason = "Изменились: " + string.Join(", ", changes);
                }
            }
            if (data.ObservedAt < listing.FirstObservedAt) listing.FirstObservedAt = data.ObservedAt;
            if (isNew) db.Listings.Add(listing);
            db.ListingObservations.Add(new()
            {
                Id = DataConventions.NewId(),
                ListingId = listing.Id,
                AgentId = agent.Id,
                JobId = job.Id,
                ObservationKey = envelope.ObservationKey,
                ContentHash = hash,
                PayloadJson = json,
                ChangesJson = JsonSerializer.Serialize(changes),
                ObservedAt = data.ObservedAt,
                RecordedAt = time.GetUtcNow()
            });
            accepted++;
        }
        job.AcceptedCount += accepted; agent.LastHeartbeatAt = time.GetUtcNow();
        job.LeaseExpiresAt = time.GetUtcNow().AddMinutes(3);
        if (result.Final)
        {
            job.State = result.Outcome switch
            {
                CollectionOutcome.Success => CollectionJobState.Completed,
                CollectionOutcome.LimitReached => CollectionJobState.LimitReached,
                CollectionOutcome.Captcha or CollectionOutcome.AuthenticationRequired or CollectionOutcome.RateLimited => CollectionJobState.AwaitingManualAction,
                CollectionOutcome.Interrupted => CollectionJobState.Interrupted,
                _ => CollectionJobState.Failed
            };
            job.ResultCode = result.Outcome.ToString(); job.CompletedAt = time.GetUtcNow();
        }
        CollectionReceipt receipt = new(result.ResultId, result.Final ? job.State.ToString() : "Accepted", accepted, duplicates);
        db.CollectionDeliveries.Add(new()
        {
            Id = result.ResultId,
            AgentId = agent.Id,
            JobId = job.Id,
            PayloadHash = payloadHash,
            ReceiptJson = JsonSerializer.Serialize(receipt, CollectionJson.Options),
            RecordedAt = time.GetUtcNow()
        });
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        return receipt;
    }

    private void EnsureLease(ServerCollectionJob job, Guid lease)
    {
        if (job.State != CollectionJobState.Leased || job.LeaseId != lease || job.LeaseExpiresAt <= time.GetUtcNow())
            throw new CollectorProtocolException("LEASE_EXPIRED_OR_REPLACED");
    }
    private static async Task<ServerCollectionJob> LockedJobAsync(LandErpDbContext db, Guid id, Guid agentId, CancellationToken cancellationToken)
    {
        var jobs = await db.CollectionJobs.FromSqlInterpolated($"SELECT * FROM collection.jobs WHERE id={id} AND agent_id={agentId} FOR UPDATE").ToListAsync(cancellationToken);
        return jobs.SingleOrDefault() ?? throw new CollectorProtocolException("WORK_NOT_ALLOWED");
    }
    private static async Task<CollectorAgent> AuthenticateAsync(LandErpDbContext db, AgentCredential credential, CancellationToken cancellationToken)
    {
        if (credential.Token.Length != 64 || !credential.Token.All(char.IsAsciiHexDigit)) throw new CollectorProtocolException("AGENT_UNAUTHORIZED");
        var agents = await db.CollectorAgents.FromSqlInterpolated($"SELECT * FROM collection.agents WHERE id={credential.AgentId} FOR UPDATE").ToListAsync(cancellationToken);
        CollectorAgent agent = agents.SingleOrDefault() ?? throw new CollectorProtocolException("AGENT_UNAUTHORIZED");
        if (!agent.Enabled || !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(agent.CredentialHash), Convert.FromHexString(Hash(credential.Token))))
            throw new CollectorProtocolException("AGENT_UNAUTHORIZED");
        return agent;
    }
    internal static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static CatalogSource MapSource(ListingSource source) => source switch
    {
        ListingSource.Avito => CatalogSource.Avito,
        ListingSource.Cian => CatalogSource.Cian,
        _ => throw new CollectorProtocolException("SOURCE_UNSUPPORTED")
    };

    private static ListingSource CollectorSource(CatalogSource source) => source switch
    {
        CatalogSource.Avito => ListingSource.Avito,
        CatalogSource.Cian => ListingSource.Cian,
        _ => throw new CollectorProtocolException("SOURCE_UNSUPPORTED")
    };

    private static void ApplyKnown(Listing listing, ListingData data, List<string> changes)
    {
        if (data.Title.Presence == FieldPresence.Present && listing.Title != data.Title.Raw) { listing.Title = data.Title.Raw; changes.Add("название"); }
        if (data.Location.Presence == FieldPresence.Present && listing.Location != data.Location.Raw) { listing.Location = data.Location.Raw; changes.Add("местоположение"); }
        if (data.Description.Presence == FieldPresence.Present && listing.Description != data.Description.Raw) { listing.Description = data.Description.Raw; changes.Add("описание"); }
        if (data.SellerName.Presence == FieldPresence.Present && listing.SellerName != data.SellerName.Raw) { listing.SellerName = data.SellerName.Raw; changes.Add("продавец"); }
        if (data.Price.Presence == FieldPresence.Present && listing.Price != DataConventions.RoundRubles(data.Price.Parsed!.Value)) { listing.Price = DataConventions.RoundRubles(data.Price.Parsed!.Value); changes.Add("цена"); }
        if (data.AreaSquareMeters.Presence == FieldPresence.Present && listing.AreaSquareMeters != decimal.Round(data.AreaSquareMeters.Parsed!.Value, 4, MidpointRounding.ToEven)) { listing.AreaSquareMeters = decimal.Round(data.AreaSquareMeters.Parsed!.Value, 4, MidpointRounding.ToEven); changes.Add("площадь"); }
        string photos = JsonSerializer.Serialize(data.PhotoUrls.Distinct());
        if (data.PhotoUrls.Length > 0 && listing.PhotosJson != photos) { listing.PhotosJson = photos; changes.Add("фотографии"); }
        listing.Url = data.Url;
    }
}
