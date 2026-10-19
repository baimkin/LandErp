using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Infrastructure.Modules.Procurement;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Modules.Catalog;

public sealed class CatalogArchiveService(IDbContextFactory<LandErpDbContext> factory,
    IEmployeeAccessService employeeAccess, IIncomingFilterPresetService presets, TimeProvider time)
    : ICatalogArchiveService
{
    private async Task<EffectiveEmployeeAccess> RequireAsync(Subject subject, CancellationToken cancellationToken)
    {
        var access = await employeeAccess.ResolveAsync(subject, cancellationToken);
        if (!access.CanProcessIncoming) throw new AccessDeniedException();
        return access;
    }

    private async Task<Listing[]> SelectAsync(LandErpDbContext db, Subject subject,
        EffectiveEmployeeAccess access, CatalogBulkSelection selection, bool excludeFromMedian,
        CancellationToken cancellationToken)
    {
        var items = await CatalogBulkSelectionReader.ReadAsync(db, presets, subject,
            access.OrganizationId, time.GetUtcNow(), selection, cancellationToken);
        if (excludeFromMedian)
        {
            Guid zoneId = selection.Filter.SearchGroupId ?? throw new ArgumentException(
                "Для исключения из медианы выберите зону поиска.");
            await CatalogCalculationService.RequireZoneAsync(db, access, zoneId, cancellationToken);
            Guid[] ids = items.Select(item => item.Id).ToArray();
            // A saved filter may replace the visible group criterion. Verify actual provenance
            // for the entire batch so this command cannot bypass median zone ownership.
            int members = await (from observation in db.ListingObservations.AsNoTracking()
                join job in db.CollectionJobs.AsNoTracking() on observation.JobId equals job.Id
                join search in db.SearchConfigurations.AsNoTracking() on job.SearchId equals search.Id
                where ids.Contains(observation.ListingId) && job.OrganizationId == access.OrganizationId
                    && search.OrganizationId == access.OrganizationId && search.SearchGroupId == zoneId
                select observation.ListingId).Distinct().CountAsync(cancellationToken);
            if (members != ids.Length) throw new AccessDeniedException();
        }
        return items;
    }

    public async Task<CatalogArchivePreview> PreviewAsync(Subject subject, CatalogBulkSelection selection,
        bool excludeFromMedian, CancellationToken cancellationToken)
    {
        var access = await RequireAsync(subject, cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var items = await SelectAsync(db, subject, access, selection, excludeFromMedian, cancellationToken);
        var result = Summarize(items, excludeFromMedian);
        return new(Stamp(items, excludeFromMedian), result.Total, result.Archived,
            result.AlreadyArchived, result.ExcludedFromMedian);
    }

    public async Task<CatalogArchiveResult> ApplyAsync(Subject subject, CatalogBulkSelection selection,
        bool excludeFromMedian, string reason, string expectedStamp, string correlationId,
        CancellationToken cancellationToken)
    {
        var access = await RequireAsync(subject, cancellationToken);
        reason = reason.Trim();
        if (reason.Length is < 3 or > 4000) throw new ArgumentException("Укажите причину длиной от 3 до 4000 символов.");
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var selected = await SelectAsync(db, subject, access, selection, excludeFromMedian, cancellationToken);
        Guid[] ids = selected.Select(item => item.Id).ToArray();
        // Recheck versions after taking row locks. Classification, median, history and audit
        // commit together; a concurrent edit must never produce a partial batch.
        Listing[] items = ids.Length == 0 ? [] : await db.Listings.FromSqlInterpolated(
            $"SELECT * FROM catalog.listings WHERE organization_id={access.OrganizationId} AND id=ANY({ids}) ORDER BY id FOR UPDATE")
            .ToArrayAsync(cancellationToken);
        if (Stamp(items, excludeFromMedian) != expectedStamp) throw CatalogBulkSelectionReader.Conflict();
        var result = Summarize(items, excludeFromMedian);
        DateTimeOffset now = time.GetUtcNow();
        foreach (var item in items)
        {
            // Keep the more specific classifications of items already in the archive.
            if (!IncomingCatalogArchive.States.Contains(item.Disposition))
                ProcurementWorkspace.ApplyCatalogDisposition(db, item, CatalogDisposition.Dismissed,
                    reason, access.OrganizationContext, subject, correlationId, now);
            if (excludeFromMedian)
                CatalogCalculationService.Change(db, item, false, subject.UserId, reason,
                    correlationId, now, selection.Filter.SearchGroupId);
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private static CatalogArchiveResult Summarize(Listing[] items, bool excludeFromMedian)
    {
        int already = items.Count(item => IncomingCatalogArchive.States.Contains(item.Disposition));
        return new(items.Length, items.Length - already, already,
            excludeFromMedian ? items.Count(item => item.IncludeInCalculation) : 0);
    }

    private static string Stamp(Listing[] items, bool excludeFromMedian) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes($"archive:{excludeFromMedian}:" + string.Join(';', items.OrderBy(item => item.Id)
            .Select(item => item.Id.ToString("N") + ":" + item.Version.ToString(CultureInfo.InvariantCulture))))));
}
