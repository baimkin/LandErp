using LandErp.Application.Foundation;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Collection.Contracts;
using LandErp.Application.Modules.Collection.Domain;
using LandErp.Application.Modules.IdentityAccess.Domain;
using LandErp.Collector.Contracts.V1;
using LandErp.Infrastructure.Modules.Catalog;
using LandErp.Infrastructure.Modules.Organization;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LandErp.Infrastructure.Modules.Collection;

/// <summary>Short database transactions serialize each agent and fence expired work. No browser dependencies.</summary>
public sealed class CollectorGateway(IDbContextFactory<LandErpDbContext> factory, TimeProvider time) : ICollectorGateway
{
    public async Task<CollectorSearchView> UpdateSearchAsync(AgentCredential credential, UpdateCollectorSearch command, CancellationToken cancellationToken)
    {
        if (command.MaxPages is < 1 or > 100 || command.Url is not { Length: > 0 and <= 2000 }
            || !ContractRules.IsSourceUrl(command.Url, command.Source)) throw new ArgumentException("COLLECTOR_SEARCH_INVALID");
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        CollectorAgent agent = await AuthenticateAsync(db, credential, cancellationToken);
        if (!agent.CanManageSearches) throw new CollectorProtocolException(CollectorErrorCodes.SearchPermissionRequired);
        var rows = await db.SearchConfigurations.FromSqlInterpolated(
            $"SELECT * FROM collection.search_configurations WHERE id={command.Id} AND organization_id={agent.OrganizationId} FOR UPDATE").ToListAsync(cancellationToken);
        SearchConfiguration search = rows.SingleOrDefault() ?? throw new CollectorProtocolException(CollectorErrorCodes.WorkNotAllowed);
        if (search.Version != command.ExpectedRevision) throw new DbUpdateConcurrencyException();
        if (await db.CollectionJobs.AnyAsync(j => j.SearchId == search.Id && (j.State == CollectionJobState.Leased || j.State == CollectionJobState.Pending), cancellationToken))
            throw new CollectorProtocolException("SEARCH_BUSY");
        if (command.GroupId != null && !await db.SearchGroups.AnyAsync(g => g.Id == command.GroupId && g.OrganizationId == agent.OrganizationId && g.Active, cancellationToken))
            throw new CollectorProtocolException(CollectorErrorCodes.WorkNotAllowed);
        search.Label = OrganizationWorkspace.ValidateName(command.Label); search.Source = MapSource(command.Source);
        search.Url = command.Url; search.MaxPages = command.MaxPages; search.SearchGroupId = command.GroupId; search.Enabled = command.Enabled;
        string zone = await db.Organizations.Where(o => o.Id == agent.OrganizationId).Select(o => o.BusinessTimeZone).SingleAsync(cancellationToken);
        CollectionScheduleRules.Apply(search, Schedule(command.Schedule), time.GetUtcNow(), zone);
        if (!search.Enabled)
        {
            ServerCollectionJob[] retries = await db.CollectionJobs
                .Where(item => item.SearchId == search.Id && item.RetryAt != null)
                .ToArrayAsync(cancellationToken);
            foreach (ServerCollectionJob retry in retries) retry.RetryAt = null;
        }
        AddMachineAudit(db, agent, "CollectionSearchUpdatedByParser", "SearchConfiguration", search.Id,
            new { search.Label, search.Enabled, search.SearchGroupId, search.ScheduleKind }, Guid.CreateVersion7());
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return SearchView(search);
    }
    public async Task<CollectorGroupView> UpdateGroupAsync(AgentCredential credential, UpdateCollectorGroup command, CancellationToken cancellationToken)
    {
        if (command.SortOrder is < 0 or > 10000) throw new ArgumentException("COLLECTOR_GROUP_INVALID");
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        CollectorAgent agent = await AuthenticateAsync(db, credential, cancellationToken);
        if (!agent.CanManageSearches) throw new CollectorProtocolException(CollectorErrorCodes.SearchPermissionRequired);
        SearchGroup group = await db.SearchGroups.SingleOrDefaultAsync(g => g.Id == command.Id && g.OrganizationId == agent.OrganizationId, cancellationToken)
            ?? throw new CollectorProtocolException(CollectorErrorCodes.WorkNotAllowed);
        if (group.Version != command.ExpectedRevision) throw new DbUpdateConcurrencyException();
        if (!command.Active && await db.SearchConfigurations.AnyAsync(s => s.SearchGroupId == group.Id && s.Enabled, cancellationToken))
            throw new CollectorProtocolException("GROUP_HAS_ACTIVE_SEARCHES");
        group.Name = OrganizationWorkspace.ValidateName(command.Name); group.SortOrder = command.SortOrder; group.Active = command.Active;
        AddMachineAudit(db, agent, "CollectionSearchGroupUpdatedByParser", "SearchGroup", group.Id, new { group.Name, group.Active }, Guid.CreateVersion7());
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        return new(group.Id, group.Name, group.SortOrder, group.Active, group.Version);
    }
    public async Task EnqueueSearchAsync(AgentCredential credential, RunCollectorSearch command, CancellationToken cancellationToken)
    {
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        CollectorAgent agent = await AuthenticateAsync(db, credential, cancellationToken);
        if (!agent.CanManageSearches) throw new CollectorProtocolException(CollectorErrorCodes.SearchPermissionRequired);
        var rows = await db.SearchConfigurations.FromSqlInterpolated(
            $"SELECT * FROM collection.search_configurations WHERE id={command.SearchId} AND organization_id={agent.OrganizationId} FOR UPDATE").ToListAsync(cancellationToken);
        SearchConfiguration search = rows.SingleOrDefault() ?? throw new CollectorProtocolException(CollectorErrorCodes.WorkNotAllowed);
        if (!search.Enabled) throw new CollectorProtocolException("SEARCH_DISABLED");
        // A repeated click or lost response must not create another active job.
        if (!await db.CollectionJobs.AnyAsync(j => j.SearchId == search.Id && (j.State == CollectionJobState.Pending || j.State == CollectionJobState.Leased), cancellationToken))
        {
            ServerCollectionJob[] retries = await db.CollectionJobs.Where(item => item.SearchId == search.Id && item.RetryAt != null).ToArrayAsync(cancellationToken);
            foreach (ServerCollectionJob retry in retries) retry.RetryAt = null;
            ServerCollectionJob job = new() { Id = DataConventions.NewId(), OrganizationId = agent.OrganizationId, SearchId = search.Id, CreatedAt = time.GetUtcNow() };
            db.CollectionJobs.Add(job);
            AddMachineAudit(db, agent, "CollectionJobQueuedByParser", "CollectionJob", job.Id, new { job.SearchId }, Guid.CreateVersion7());
            await db.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }
    public async Task<CollectorWorkspace> ReadWorkspaceAsync(AgentCredential credential, CancellationToken cancellationToken)
    {
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        CollectorAgent agent = await AuthenticateAsync(db, credential, cancellationToken);
        if (!agent.CanManageSearches) throw new CollectorProtocolException(CollectorErrorCodes.SearchPermissionRequired);
        CollectorGroupView[] groups = await db.SearchGroups.Where(item => item.OrganizationId == agent.OrganizationId)
            .OrderBy(item => item.SortOrder).ThenBy(item => item.Name)
            .Select(item => new CollectorGroupView(item.Id, item.Name, item.SortOrder, item.Active, item.Version))
            .ToArrayAsync(cancellationToken);
        SearchConfiguration[] values = await db.SearchConfigurations.Where(item => item.OrganizationId == agent.OrganizationId)
            .OrderBy(item => item.Label).ToArrayAsync(cancellationToken);
        CollectorSearchView[] searches = values.Select(SearchView).ToArray();
        return new(groups, searches, [CollectorFeatures.SearchRead, CollectorFeatures.SearchManage]);
    }

    public async Task<CollectorGroupView> CreateGroupAsync(AgentCredential credential, CreateCollectorGroup command, CancellationToken cancellationToken)
    {
        if (command.CommandId == Guid.Empty || command.SortOrder is < 0 or > 10000) throw new ArgumentException("COLLECTOR_GROUP_INVALID");
        string name = OrganizationWorkspace.ValidateName(command.Name);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        CollectorAgent agent = await AuthenticateAsync(db, credential, cancellationToken);
        if (!agent.CanManageSearches) throw new CollectorProtocolException(CollectorErrorCodes.SearchPermissionRequired);
        SearchGroup? existing = await db.SearchGroups.SingleOrDefaultAsync(item => item.Id == command.CommandId, cancellationToken);
        if (existing != null)
        {
            if (existing.OrganizationId != agent.OrganizationId || existing.Name != name || existing.SortOrder != command.SortOrder)
                throw new CollectorProtocolException(CollectorErrorCodes.IdempotencyConflict);
            await transaction.CommitAsync(cancellationToken);
            return new(existing.Id, existing.Name, existing.SortOrder, existing.Active, existing.Version);
        }
        SearchGroup group = new() { Id = command.CommandId, OrganizationId = agent.OrganizationId, Name = name,
            SortOrder = command.SortOrder, RecordedAt = time.GetUtcNow() };
        db.SearchGroups.Add(group);
        db.SearchGroupMarketSettings.Add(new() { Id = DataConventions.NewId(), OrganizationId = agent.OrganizationId,
            SearchGroupId = group.Id, PeriodDays = 30, AllowedPropertyTypes = Enum.GetNames<IncomingLandType>() });
        AddMachineAudit(db, agent, "CollectionSearchGroupCreatedByParser", "SearchGroup", group.Id,
            new { group.Name, group.SortOrder }, command.CommandId);
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        return new(group.Id, group.Name, group.SortOrder, group.Active, group.Version);
    }

    public async Task<CollectorSearchView> CreateSearchAsync(AgentCredential credential, CreateCollectorSearch command, CancellationToken cancellationToken)
    {
        if (command.CommandId == Guid.Empty || command.MaxPages is < 1 or > 100 || command.Url.Length > 2000
            || !ContractRules.IsSourceUrl(command.Url, command.Source)) throw new ArgumentException("COLLECTOR_SEARCH_INVALID");
        string label = OrganizationWorkspace.ValidateName(command.Label);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        CollectorAgent agent = await AuthenticateAsync(db, credential, cancellationToken);
        if (!agent.CanManageSearches) throw new CollectorProtocolException(CollectorErrorCodes.SearchPermissionRequired);
        SearchConfiguration? existing = await db.SearchConfigurations.SingleOrDefaultAsync(item => item.Id == command.CommandId, cancellationToken);
        if (existing != null)
        {
            if (existing.OrganizationId != agent.OrganizationId || existing.Label != label || existing.Url != command.Url
                || existing.Source != MapSource(command.Source) || existing.MaxPages != command.MaxPages || existing.SearchGroupId != command.GroupId
                || (CollectorScheduleKind)existing.ScheduleKind != command.Schedule.Kind || existing.IntervalMinutes != command.Schedule.IntervalMinutes
                || !(JsonSerializer.Deserialize<string[]>(existing.FixedTimesJson) ?? []).SequenceEqual((command.Schedule.FixedTimes ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)))
                throw new CollectorProtocolException(CollectorErrorCodes.IdempotencyConflict);
            await transaction.CommitAsync(cancellationToken); return SearchView(existing);
        }
        if (command.GroupId != null && !await db.SearchGroups.AnyAsync(item => item.Id == command.GroupId
            && item.OrganizationId == agent.OrganizationId && item.Active, cancellationToken))
            throw new CollectorProtocolException(CollectorErrorCodes.WorkNotAllowed);
        string zone = await db.Organizations.Where(item => item.Id == agent.OrganizationId)
            .Select(item => item.BusinessTimeZone).SingleAsync(cancellationToken);
        SearchConfiguration search = new() { Id = command.CommandId, OrganizationId = agent.OrganizationId,
            Label = label, Source = MapSource(command.Source), Url = command.Url, MaxPages = command.MaxPages,
            SearchGroupId = command.GroupId };
        CollectionScheduleRules.Apply(search, Schedule(command.Schedule), time.GetUtcNow(), zone);
        db.SearchConfigurations.Add(search);
        AddMachineAudit(db, agent, "CollectionSearchCreatedByParser", "SearchConfiguration", search.Id,
            new { search.Label, search.Source, search.MaxPages, search.SearchGroupId, search.ScheduleKind }, command.CommandId);
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        return SearchView(search);
    }

    public async Task<AgentActivationReceipt> ActivateAsync(AgentActivation activation, CancellationToken cancellationToken)
    {
        ValidateRegistration(activation.ContractVersion, activation.Version, activation.Capabilities);
        if (activation.AgentId == Guid.Empty || activation.ActivationSecret is not { Length: 64 }
            || !activation.ActivationSecret.All(char.IsAsciiHexDigit)
            || string.IsNullOrWhiteSpace(activation.MachineName) || activation.MachineName.Length > 200)
            throw new CollectorProtocolException("ACTIVATION_INVALID");
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var agents = await db.CollectorAgents.FromSqlInterpolated($"SELECT * FROM collection.agents WHERE id={activation.AgentId} FOR UPDATE").ToListAsync(cancellationToken);
        CollectorAgent agent = agents.SingleOrDefault() ?? throw new CollectorProtocolException("ACTIVATION_INVALID");
        if (!agent.Enabled) throw new CollectorProtocolException("AGENT_UNAUTHORIZED");
        if (agent.ActivationUsedAt != null) throw new CollectorProtocolException("ACTIVATION_USED");
        if (agent.ActivationExpiresAt <= time.GetUtcNow()) throw new CollectorProtocolException("ACTIVATION_EXPIRED");
        if (agent.ActivationHash.Length != 64 || !CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(agent.ActivationHash), Convert.FromHexString(Hash(activation.ActivationSecret))))
            throw new CollectorProtocolException("ACTIVATION_INVALID");
        string credential = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        agent.CredentialHash = Hash(credential);
        agent.ActivationUsedAt = time.GetUtcNow();
        agent.VersionText = activation.Version;
        agent.Capabilities = string.Join(',', activation.Capabilities.Distinct().Order());
        agent.RegisteredAt = time.GetUtcNow(); agent.LastHeartbeatAt = time.GetUtcNow();
        agent.RuntimeState = AgentRuntimeState.Idle;
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        return new(agent.Id, credential, 1, agent.Name);
    }

    public async Task RegisterAsync(AgentCredential credential, AgentRegistration registration, CancellationToken cancellationToken)
    {
        ValidateRegistration(registration.ContractVersion, registration.Version, registration.Capabilities);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        CollectorAgent agent = await AuthenticateAsync(db, credential, cancellationToken);
        agent.VersionText = registration.Version;
        agent.Capabilities = string.Join(',', registration.Capabilities.Distinct().Order());
        agent.RegisteredAt ??= time.GetUtcNow(); agent.LastHeartbeatAt = time.GetUtcNow();
        agent.RuntimeState = AgentRuntimeState.Idle;
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
    }

    public async Task HeartbeatAsync(AgentCredential credential, AgentHeartbeat heartbeat, CancellationToken cancellationToken)
    {
        if (heartbeat.SourceStatus != null && !Enum.IsDefined(heartbeat.SourceStatus.Value)
            || heartbeat.RuntimeState != null && !Enum.IsDefined(heartbeat.RuntimeState.Value)
            || heartbeat.SourceState != null && !Enum.IsDefined(heartbeat.SourceState.Value)
            || heartbeat.Progress is { } progress && (!Enum.IsDefined(progress.Phase) || progress.Page is < 0
                || progress.MaxPages is < 1 or > 100 || progress.ProcessedCount < 0 || progress.TotalCount < 0
                || progress.TotalCount != null && progress.ProcessedCount > progress.TotalCount
                || progress.Page != null && progress.MaxPages != null && progress.Page > progress.MaxPages
                || progress.LastUsefulActionAt is { } activity && activity.Offset != TimeSpan.Zero))
            throw new ArgumentException("HEARTBEAT_STATE_INVALID");
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        CollectorAgent agent = await AuthenticateAsync(db, credential, cancellationToken);
        agent.LastHeartbeatAt = time.GetUtcNow();
        if (heartbeat.RuntimeState != null) agent.RuntimeState = heartbeat.RuntimeState.Value;
        if (heartbeat.SourceState == SourceRuntimeState.Ready) agent.AttentionCode = "";
        else if (heartbeat.SourceState != null) agent.AttentionCode = heartbeat.SourceState.Value.ToString();
        else if (heartbeat.SourceStatus != null) agent.AttentionCode = heartbeat.SourceStatus.Value.ToString();
        ApplyProgress(agent, heartbeat.Progress);
        if (heartbeat.JobId != null || heartbeat.LeaseId != null)
        {
            ServerCollectionJob job = await LockedJobAsync(db, heartbeat.JobId ?? Guid.Empty, agent.OrganizationId, cancellationToken);
            EnsureLease(job, agent.Id, heartbeat.LeaseId ?? Guid.Empty, false);
            job.LeaseExpiresAt = time.GetUtcNow().AddMinutes(3);
            if (heartbeat.SourceState != null) job.ResultCode = heartbeat.SourceState.Value.ToString();
            else if (heartbeat.SourceStatus != null) job.ResultCode = heartbeat.SourceStatus.Value.ToString();
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
        // AuthenticateAsync holds a row lock on the Agent. Concurrent claims from the same machine
        // therefore serialize here and always return its one still-valid lease.
        var active = await db.CollectionJobs.FromSqlInterpolated($"""
            SELECT * FROM collection.jobs
            WHERE agent_id={agent.Id} AND state='Leased' AND lease_expires_at>{now}
            ORDER BY created_at FOR UPDATE
            """).ToListAsync(cancellationToken);
        if (active.Count > 1) throw new CollectorProtocolException("AGENT_MULTIPLE_ACTIVE_WORK");
        if (active.Count == 1)
        {
            agent.LastHeartbeatAt = now;
            SearchConfiguration activeSearch = await db.SearchConfigurations.SingleAsync(item => item.Id == active[0].SearchId, cancellationToken);
            await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
            return Work(active[0], activeSearch);
        }
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
        agent.LastHeartbeatAt = now; agent.RuntimeState = AgentRuntimeState.Claiming;
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        return Work(job, search);
    }

    public async Task<CollectionReceipt> AcceptAsync(AgentCredential credential, CollectionResult result, CancellationToken cancellationToken)
    {
        if (result.ResultId == Guid.Empty || result.Observations == null || result.Observations.Length > 25 || !Enum.IsDefined(result.Outcome)
            || !result.Final && (result.Outcome != CollectionOutcome.Success || result.Coverage != null
                || !string.IsNullOrEmpty(result.ReasonCode) || result.Warnings is { Length: > 0 }))
            throw new ArgumentException("COLLECTION_RESULT_INVALID");
        ValidateFinalResult(result);
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
        ServerCollectionJob job = await LockedJobAsync(db, result.JobId, agent.OrganizationId, cancellationToken);
        EnsureLease(job, agent.Id, result.LeaseId, true);
        SearchConfiguration search = await db.SearchConfigurations.SingleAsync(item => item.Id == job.SearchId, cancellationToken);
        if (result.Observations.Any(item => MapSource(item.Data.Source) != search.Source)) throw new ArgumentException("SOURCE_MISMATCH");
        // Global ordering of natural identity locks avoids two overlapping search batches deadlocking.
        foreach (string identity in result.Observations.Select(item => $"{agent.OrganizationId}:{item.Data.Source}:{item.Data.ExternalId}").Distinct().Order(StringComparer.Ordinal))
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({identity},0))", cancellationToken);
        int accepted = 0; int duplicates = 0; int newListings = 0; int changedListings = 0;
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
                ChangedAt = time.GetUtcNow(),
                AttentionRequired = true,
                AttentionAt = time.GetUtcNow()
            };
            if (await db.ListingObservations.AnyAsync(item => item.ListingId == listing.Id && item.ObservedAt == data.ObservedAt && item.ContentHash == hash, cancellationToken)
                || db.ListingObservations.Local.Any(item => item.ListingId == listing.Id && item.ObservedAt == data.ObservedAt && item.ContentHash == hash))
            { duplicates++; continue; }
            List<string> changes = [];
            if (isNew || data.ObservedAt > listing.LastObservedAt)
            {
                ApplyKnown(listing, data, changes);
                IncomingDuplicateDetector.ApplyExtractedCadastral(listing);
                listing.LastObservedAt = data.ObservedAt;
                if (changes.Count > 0 && !isNew)
                {
                    listing.DataRevision++; listing.ChangedAt = time.GetUtcNow();
                    listing.QueueReason = "Изменились: " + string.Join(", ", changes);
                    if (listing.Disposition != CatalogDisposition.Monitoring)
                    {
                        listing.AttentionRequired = true;
                        listing.AttentionAt = time.GetUtcNow();
                    }
                    db.CatalogEvents.Add(NewCatalogEvent(listing, CatalogEventKind.SourceChanged,
                        listing.QueueReason, time.GetUtcNow()));
                }
                CatalogMonitoringEvaluator.Evaluate(db, listing, time.GetUtcNow());
            }
            if (data.ObservedAt < listing.FirstObservedAt) listing.FirstObservedAt = data.ObservedAt;
            if (isNew) db.Listings.Add(listing);
            await IncomingDuplicateDetector.RefreshAsync(db, listing, time.GetUtcNow(), cancellationToken);
            if (isNew) newListings++;
            else if (changes.Count > 0) changedListings++;
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
        job.ProcessedCount += result.Observations.Length; job.AcceptedCount += accepted;
        job.NewListingsCount += newListings; job.ChangedListingsCount += changedListings; agent.LastHeartbeatAt = time.GetUtcNow();
        job.LeaseExpiresAt = time.GetUtcNow().AddMinutes(3);
        if (result.Final)
        {
            job.State = result.Outcome switch
            {
                CollectionOutcome.Success => CollectionJobState.Completed,
                CollectionOutcome.LimitReached => CollectionJobState.LimitReached,
                CollectionOutcome.Partial => CollectionJobState.Partial,
                CollectionOutcome.RateLimited => CollectionJobState.RateLimited,
                CollectionOutcome.Captcha or CollectionOutcome.AuthenticationRequired => CollectionJobState.AwaitingManualAction,
                CollectionOutcome.Interrupted => CollectionJobState.Interrupted,
                _ => CollectionJobState.Failed
            };
            DateTimeOffset completedAt = time.GetUtcNow();
            job.ResultCode = result.Outcome.ToString(); job.ReasonCode = result.ReasonCode; job.CompletedAt = completedAt;
            job.WarningsJson = JsonSerializer.Serialize((result.Warnings ?? []).Distinct(StringComparer.Ordinal).ToArray(), CollectionJson.Options);
            job.CoverageJson = result.Coverage == null ? null : JsonSerializer.Serialize(result.Coverage, CollectionJson.Options);
            CollectionRetryDecision retry = CollectionRetryPolicy.Decide(result.Outcome, result.ReasonCode, job.RetryAttempt);
            job.RetryAt = retry.Delay is null ? null : completedAt.Add(retry.Delay.Value);
            job.RequiresOperatorAttention = retry.AttentionRequired;
            if (result.Outcome is CollectionOutcome.Success or CollectionOutcome.LimitReached)
            {
                search.ConsecutiveFailures = 0;
                ServerCollectionJob[] resolved = await db.CollectionJobs
                    .Where(item => item.SearchId == search.Id && item.RequiresOperatorAttention)
                    .ToArrayAsync(cancellationToken);
                foreach (ServerCollectionJob previous in resolved) previous.RequiresOperatorAttention = false;
            }
            else search.ConsecutiveFailures++;
            bool manualAction = result.Outcome is CollectionOutcome.Captcha or CollectionOutcome.AuthenticationRequired;
            agent.RuntimeState = manualAction ? AgentRuntimeState.AwaitingManualAction : AgentRuntimeState.Idle;
            agent.AttentionCode = manualAction ? string.IsNullOrEmpty(result.ReasonCode) ? result.Outcome.ToString() : result.ReasonCode : "";
            ApplyProgress(agent, null);
        }
        CollectionReceipt receipt = new(result.ResultId, result.Final ? job.State.ToString() : "Accepted",
            accepted, duplicates, newListings, changedListings);
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

    private static void ValidateFinalResult(CollectionResult result)
    {
        if (!result.Final) return;
        if (result.ReasonCode.Length > 64 || result.ReasonCode.Any(character => !(char.IsAsciiLetterUpper(character) || char.IsDigit(character) || character == '_'))
            || result.Warnings is { Length: > 20 } || (result.Warnings ?? []).Any(warning => string.IsNullOrWhiteSpace(warning)
                || warning.Length > 64 || warning.Any(character => !(char.IsAsciiLetterUpper(character) || char.IsDigit(character) || character == '_'))))
            throw new ArgumentException("COLLECTION_RESULT_REASON_INVALID");
        if (result.Coverage is not { } coverage) return;
        if (coverage.UniqueObserved < 0 || coverage.SourceCountHint < 0 || coverage.StableRounds < 0
            || coverage.CompletedPages < 0 || coverage.RequestedPageLimit is < 1 or > 100
            || coverage.CompletedPages > coverage.RequestedPageLimit || coverage.ResponseBatches < 0
            || result.Outcome == CollectionOutcome.Success && (!coverage.EndReached || !coverage.LoadingCompleted))
            throw new ArgumentException("COLLECTION_COVERAGE_INVALID");
    }

    private void EnsureLease(ServerCollectionJob job, Guid agentId, Guid lease, bool acceptingResult)
    {
        if (job.State != CollectionJobState.Leased)
            throw new CollectorProtocolException(acceptingResult ? "RESULT_SUPERSEDED" : "WORK_NOT_ACTIVE");
        if (job.LeaseExpiresAt <= time.GetUtcNow()) throw new CollectorProtocolException("LEASE_EXPIRED");
        if (job.AgentId != agentId || job.LeaseId != lease) throw new CollectorProtocolException("LEASE_REPLACED");
    }
    private static async Task<ServerCollectionJob> LockedJobAsync(LandErpDbContext db, Guid id, Guid organizationId, CancellationToken cancellationToken)
    {
        var jobs = await db.CollectionJobs.FromSqlInterpolated($"SELECT * FROM collection.jobs WHERE id={id} AND organization_id={organizationId} FOR UPDATE").ToListAsync(cancellationToken);
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

    private void AddMachineAudit(LandErpDbContext db, CollectorAgent agent, string action, string type, Guid objectId,
        object changes, Guid commandId) => db.AuditEvents.Add(new AuditEvent
        {
            Id = DataConventions.NewId(), OrganizationId = agent.OrganizationId, ActorId = agent.Id,
            Action = action, ObjectType = type, ObjectId = objectId,
            Changes = JsonSerializer.Serialize(changes), CorrelationId = commandId.ToString(), RecordedAt = time.GetUtcNow()
        });

    private static CollectorSearchView SearchView(SearchConfiguration search) => new(search.Id, search.Label,
        CollectorSource(search.Source), search.Url, search.MaxPages, search.SearchGroupId,
        new((CollectorScheduleKind)search.ScheduleKind, search.IntervalMinutes,
            JsonSerializer.Deserialize<string[]>(search.FixedTimesJson) ?? []), search.Enabled, search.Version);
    private static CollectionSchedule Schedule(CollectorScheduleDefinition schedule)
    {
        if (schedule == null || !Enum.IsDefined(schedule.Kind)
            || schedule.Kind == CollectorScheduleKind.Interval && schedule.IntervalMinutes is not (>= 5 and <= 10080))
            throw new ArgumentException("COLLECTOR_SCHEDULE_INVALID");
        return new((CollectionScheduleKind)schedule.Kind, schedule.IntervalMinutes, schedule.FixedTimes);
    }

    private static void ValidateRegistration(int contractVersion, string version, ListingSource[] capabilities)
    {
        if (contractVersion != 1 || string.IsNullOrWhiteSpace(version) || version.Length > 32
            || capabilities == null || capabilities.Length is < 1 or > 2
            || capabilities.Any(value => !Enum.IsDefined(value)))
            throw new CollectorProtocolException("VERSION_OR_CAPABILITY_UNSUPPORTED");
    }

    private static void ApplyProgress(CollectorAgent agent, CollectionProgress? progress)
    {
        agent.ProgressProcessed = progress?.ProcessedCount ?? 0;
        agent.ProgressTotal = progress?.TotalCount;
        agent.ProgressCurrentPage = progress?.Page;
        agent.ProgressMaxPages = progress?.MaxPages;
        // V1 contract: null means "not reported", not "erase known activity".
        // Keep the most recent useful-action timestamp across early heartbeats and
        // after terminal result cleanup; only a newer explicit value advances it.
        if (progress?.LastUsefulActionAt is { } useful
            && (agent.LastActivityAt == null || useful > agent.LastActivityAt.Value))
            agent.LastActivityAt = useful;
    }

    private static CollectionWork Work(ServerCollectionJob job, SearchConfiguration search) =>
        new(job.Id, job.LeaseId!.Value, job.LeaseExpiresAt!.Value, CollectorSource(search.Source), search.Url, search.MaxPages, search.Label);

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

    private static decimal? PricePerSotka(decimal? price, decimal? areaSquareMeters) =>
        CatalogMonitoringEvaluator.PricePerSotka(price, areaSquareMeters);

    private static CatalogEvent NewCatalogEvent(Listing listing, CatalogEventKind kind, string message, DateTimeOffset now) => new()
    {
        Id = DataConventions.NewId(), OrganizationId = listing.OrganizationId, CatalogItemId = listing.Id,
        Kind = kind, Message = message, ObservedPrice = listing.Price,
        ObservedPricePerSotka = PricePerSotka(listing.Price, listing.AreaSquareMeters), RecordedAt = now
    };
}
