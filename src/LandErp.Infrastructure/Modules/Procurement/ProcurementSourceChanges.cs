using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Collector.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using LandErp.Infrastructure.Modules.Catalog;
using LandErp.Infrastructure.Persistence;

namespace LandErp.Infrastructure.Modules.Procurement;

public sealed partial class ProcurementWorkspace
{
    public async Task<IReadOnlyList<SourceChangeNotice>> ReadSourceChangeNoticesAsync(Subject subject, Guid caseId, CancellationToken cancellationToken)
    {
        var access = await RequireProcurementAsync(subject, ProcurementAccessLevel.Read, cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        if (!await VisibleReadCases(db, access).AnyAsync(x => x.Case.Id == caseId, cancellationToken)) throw new AccessDeniedException();
        return await (from link in db.PropertyCaseSourceLinks.AsNoTracking()
                      join source in db.Listings.AsNoTracking() on link.CatalogItemId equals source.Id
                      where link.OrganizationId == access.ProcurementReadContext.OrganizationId && source.OrganizationId == link.OrganizationId
                          && link.PropertyCaseId == caseId && link.Confirmed && source.DataRevision > 1 && source.DataRevision > link.ViewedDataRevision
                      orderby source.Source, source.Id
                      select new SourceChangeNotice(source.Id, source.Source, source.Title ?? "Объявление")).ToArrayAsync(cancellationToken);
    }

    public async Task AcknowledgeSourceChangesAsync(Subject subject, AcknowledgeSourceChanges command, CancellationToken cancellationToken)
    {
        if (command.Revision < 1) throw new ArgumentException("Некорректная версия просмотра.");
        var access = await RequireProcurementAsync(subject, ProcurementAccessLevel.Read, cancellationToken);
        var context = access.ProcurementReadContext;
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await EmployeeWorkInvariant.LockOrganizationAsync(db, context.OrganizationId, cancellationToken);
        await EnsureActiveEmployeeAsync(db, context.EmployeeId, cancellationToken);
        if (!await VisibleReadCases(db, access).AnyAsync(x => x.Case.Id == command.CaseId, cancellationToken)) throw new AccessDeniedException();
        var links = db.PropertyCaseSourceLinks.Where(x => x.Id == command.LinkId && x.OrganizationId == context.OrganizationId
            && x.PropertyCaseId == command.CaseId && x.CatalogItemId == command.CatalogItemId && x.Confirmed);
        if (!await links.AnyAsync(cancellationToken)) throw new AccessDeniedException();
        long current = await db.Listings.Where(x => x.Id == command.CatalogItemId && x.OrganizationId == context.OrganizationId)
            .Select(x => x.DataRevision).SingleAsync(cancellationToken);
        if (command.Revision > current) throw new DbUpdateConcurrencyException();
        // Only the displayed bound advances. Retries/older windows cannot move the receipt backwards or consume newer data.
        await links.ExecuteUpdateAsync(set => set.SetProperty(x => x.ViewedDataRevision,
            x => x.ViewedDataRevision < command.Revision ? command.Revision : x.ViewedDataRevision), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<SourceChangeComparison> ReadSourceChangesAsync(Subject subject, Guid caseId, Guid catalogItemId, CancellationToken cancellationToken)
    {
        var access = await RequireProcurementAsync(subject, ProcurementAccessLevel.Read, cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        // Revision, source link and observations must come from one database snapshot.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        if (!await VisibleReadCases(db, access).AnyAsync(x => x.Case.Id == caseId, cancellationToken)) throw new AccessDeniedException();
        var link = await db.PropertyCaseSourceLinks.AsNoTracking().SingleOrDefaultAsync(x =>
            x.OrganizationId == access.ProcurementReadContext.OrganizationId && x.PropertyCaseId == caseId
            && x.CatalogItemId == catalogItemId && x.Confirmed, cancellationToken) ?? throw new AccessDeniedException();
        var source = await db.Listings.AsNoTracking().SingleAsync(x => x.Id == catalogItemId && x.OrganizationId == link.OrganizationId, cancellationToken);
        var saved = await db.ListingObservations.AsNoTracking().Where(x => x.ListingId == catalogItemId)
            .OrderBy(x => x.RecordedAt).ThenBy(x => x.Id).ToArrayAsync(cancellationToken);
        // Invalid payload is a load failure, not an empty successful comparison eligible for acknowledgement.
        var observations = saved.Select(x => new ObservationView(x.Id, x.ListingId, x.ObservedAt, x.RecordedAt,
            JsonSerializer.Deserialize<ListingData>(x.PayloadJson, CollectionJson.Options) ?? throw new JsonException(),
            JsonSerializer.Deserialize<string[]>(x.ChangesJson) ?? throw new JsonException())).ToArray();
        DateTimeOffset? latest = null;
        int revisions = 0;
        foreach (var item in observations)
        {
            if (latest != null && item.ObservedAt <= latest) continue;
            if (latest == null || item.Changes.Length > 0) revisions++;
            latest = item.ObservedAt;
        }
        bool complete = observations.Length > 0 && revisions == source.DataRevision
            && observations[0].ObservedAt == source.FirstObservedAt && latest == source.LastObservedAt;

        CatalogPhotoFingerprint[] fingerprints = await db.CatalogPhotoFingerprints.AsNoTracking()
            .Where(x => x.OrganizationId == link.OrganizationId && x.ListingId == catalogItemId
                && x.Status == PhotoFingerprintStatus.Ready && x.PerceptualHash != null)
            .ToArrayAsync(cancellationToken);
        Dictionary<string, long> photoFingerprints = new(StringComparer.Ordinal);
        if (fingerprints.Length > 0)
        {
            Dictionary<string, long> byUrlHash = fingerprints.ToDictionary(
                x => x.UrlHash, x => x.PerceptualHash!.Value, StringComparer.Ordinal);
            foreach (string url in observations.SelectMany(x => x.Data.PhotoUrls)
                         .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal))
            {
                if (byUrlHash.TryGetValue(SourceChangePhotoUrlHash(url), out long fingerprint))
                    photoFingerprints[url] = fingerprint;
            }
        }

        DuplicateDetectionSettingsValues duplicateSettings =
            await DuplicateDetectionSettingsService.ReadValuesAsync(db, link.OrganizationId, cancellationToken);
        var changes = SourceObservationComparison.Build(
            observations, complete, photoFingerprints, duplicateSettings.PhotoHammingDistance);
        if (observations.Length == 0)
        {
            // Legacy price events are independently stored before/after evidence; never substitute working facts.
            var events = await db.CatalogEvents.AsNoTracking().Where(x => x.CatalogItemId == catalogItemId
                && x.OrganizationId == link.OrganizationId && x.Kind == CatalogEventKind.SourceChanged
                && x.PreviousObservedPrice != x.ObservedPrice).OrderByDescending(x => x.RecordedAt).ToArrayAsync(cancellationToken);
            changes = events.Select(x => new SourceChangeObservation(x.Id, x.RecordedAt,
                [new("цена", x.PreviousObservedPrice is {} before ? new(Number: before) : null,
                    x.ObservedPrice is {} after ? new(Number: after) : null)])).ToArray();
        }
        await transaction.CommitAsync(cancellationToken);
        return new(caseId, link.Id, source.Id, source.Source, source.Title ?? "Объявление", source.Url,
            source.DataRevision, source.LastObservedAt, complete, changes)
            { CanAcknowledge = observations.Length > 0 && latest == source.LastObservedAt };
    }

    private static string SourceChangePhotoUrlHash(string url) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
}
