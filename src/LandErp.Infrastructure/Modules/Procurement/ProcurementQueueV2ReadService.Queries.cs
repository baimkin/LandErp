using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Modules.Procurement;

public sealed partial class ProcurementQueueV2ReadService
{
    private static IQueryable<Row> VisibleCases(LandErpDbContext db, AccessContext context)
    {
        IQueryable<PropertyCase> cases = ProcurementVisibility.Apply(db.PropertyCases.AsNoTracking(), db, context);
        IQueryable<Row> rows = from item in cases
                               join assignment in db.WorkAssignments.AsNoTracking() on item.AssignmentId equals assignment.Id
                               join task in db.WorkTasks.AsNoTracking() on item.WorkTaskId equals task.Id
                               select new Row { Case = item, Assignment = assignment, Task = task };
        return rows;
    }

    private static IQueryable<Row> VisibleReadCases(LandErpDbContext db, EffectiveEmployeeAccess access)
    {
        IQueryable<PropertyCase> cases = ProcurementVisibility.ApplyRead(
            db.PropertyCases.AsNoTracking(), db, access);
        IQueryable<Row> rows = from item in cases
                               join assignment in db.WorkAssignments.AsNoTracking() on item.AssignmentId equals assignment.Id
                               join task in db.WorkTasks.AsNoTracking() on item.WorkTaskId equals task.Id
                               select new Row { Case = item, Assignment = assignment, Task = task };
        return rows;
    }

    private static IQueryable<Row> WhereSourceChanged(IQueryable<Row> query, LandErpDbContext db) =>
        query.Where(row => db.PropertyCaseSourceLinks.Any(link => link.PropertyCaseId == row.Case.Id && link.Confirmed
            && db.Listings.Any(source => source.Id == link.CatalogItemId && source.DataRevision > link.ReviewedDataRevision)));

    private static IQueryable<Row> ApplyCheckFilter(IQueryable<Row> query, ProcurementQueueV2CheckFilter filter, LandErpDbContext db) => filter switch
    {
        ProcurementQueueV2CheckFilter.HasIssues => query.Where(row => db.CaseChecks.Any(item => item.PropertyCaseId == row.Case.Id
            && item.Level == CaseCheckLevel.Quick && (item.Status == CaseCheckStatus.Issue || item.Status == CaseCheckStatus.Blocked || item.Blocker))),
        ProcurementQueueV2CheckFilter.Incomplete => query.Where(row => !db.CaseChecks.Any(item => item.PropertyCaseId == row.Case.Id && item.Level == CaseCheckLevel.Quick)
            || db.CaseChecks.Any(item => item.PropertyCaseId == row.Case.Id && item.Level == CaseCheckLevel.Quick
                && (item.Status == CaseCheckStatus.Planned || item.Status == CaseCheckStatus.InProgress))),
        ProcurementQueueV2CheckFilter.Complete => query.Where(row => db.CaseChecks.Any(item => item.PropertyCaseId == row.Case.Id && item.Level == CaseCheckLevel.Quick)
            && !db.CaseChecks.Any(item => item.PropertyCaseId == row.Case.Id && item.Level == CaseCheckLevel.Quick
                && (item.Status != CaseCheckStatus.Passed || item.Blocker))),
        _ => query
    };

    private static IQueryable<Row> ApplySort(IQueryable<Row> query, ProcurementQueueV2Filter filter, LandErpDbContext db) => (filter.Sort, filter.Descending) switch
    {
        (ProcurementQueueV2Sort.DueAt, false) => query.OrderBy(row => row.Task.DueAt).ThenBy(row => row.Case.BusinessNumber),
        (ProcurementQueueV2Sort.DueAt, true) => query.OrderByDescending(row => row.Task.DueAt).ThenBy(row => row.Case.BusinessNumber),
        (ProcurementQueueV2Sort.WorkingPrice, false) => query.OrderBy(row => row.Case.WorkingPrice).ThenBy(row => row.Case.BusinessNumber),
        (ProcurementQueueV2Sort.WorkingPrice, true) => query.OrderByDescending(row => row.Case.WorkingPrice).ThenBy(row => row.Case.BusinessNumber),
        (ProcurementQueueV2Sort.Area, false) => query.OrderBy(row => row.Case.WorkingAreaSquareMeters).ThenBy(row => row.Case.BusinessNumber),
        (ProcurementQueueV2Sort.Area, true) => query.OrderByDescending(row => row.Case.WorkingAreaSquareMeters).ThenBy(row => row.Case.BusinessNumber),
        (ProcurementQueueV2Sort.LastContact, false) => query.OrderBy(row => db.CaseNegotiations.Where(item => item.PropertyCaseId == row.Case.Id)
            .Max(item => (DateTimeOffset?)item.EffectiveAt)).ThenBy(row => row.Case.BusinessNumber),
        (ProcurementQueueV2Sort.LastContact, true) => query.OrderByDescending(row => db.CaseNegotiations.Where(item => item.PropertyCaseId == row.Case.Id)
            .Max(item => (DateTimeOffset?)item.EffectiveAt)).ThenBy(row => row.Case.BusinessNumber),
        (_, false) => query.OrderBy(row => row.Case.RecordedAt).ThenBy(row => row.Case.BusinessNumber),
        _ => query.OrderByDescending(row => row.Case.RecordedAt).ThenBy(row => row.Case.BusinessNumber)
    };

    private static IQueryable<SourceDb> SourceRows(LandErpDbContext db, Guid organizationId, Guid[] caseIds) =>
        from link in db.PropertyCaseSourceLinks.AsNoTracking()
        join source in db.Listings.AsNoTracking() on link.CatalogItemId equals source.Id
        where link.OrganizationId == organizationId && source.OrganizationId == organizationId && link.Confirmed && caseIds.Contains(link.PropertyCaseId)
        select new SourceDb(link.PropertyCaseId, source.Id, source.Source, source.ExternalId, source.Url, source.Title, source.Price, source.Currency,
            source.AreaSquareMeters, source.Location, source.CadastralNumber, source.PhotosJson, link.Provenance, source.LastObservedAt, source.ChangedAt,
            source.DataRevision, link.ReviewedDataRevision, link.RecordedAt);

    private static IQueryable<ContactDb> LatestContacts(LandErpDbContext db, Guid[] caseIds) =>
        db.PropertyCases.AsNoTracking().Where(item => caseIds.Contains(item.Id)).Select(item => new ContactDb(item.Id,
            db.CaseNegotiations.Where(entry => entry.PropertyCaseId == item.Id).OrderByDescending(entry => entry.EffectiveAt).ThenByDescending(entry => entry.RecordedAt)
                .Select(entry => (DateTimeOffset?)entry.EffectiveAt).FirstOrDefault(),
            db.CaseNegotiations.Where(entry => entry.PropertyCaseId == item.Id).OrderByDescending(entry => entry.EffectiveAt).ThenByDescending(entry => entry.RecordedAt)
                .Select(entry => entry.Channel).FirstOrDefault(),
            db.CaseNegotiations.Where(entry => entry.PropertyCaseId == item.Id).OrderByDescending(entry => entry.EffectiveAt).ThenByDescending(entry => entry.RecordedAt)
                .Select(entry => entry.Outcome).FirstOrDefault(),
            db.CaseNegotiations.Where(entry => entry.PropertyCaseId == item.Id).OrderByDescending(entry => entry.EffectiveAt).ThenByDescending(entry => entry.RecordedAt)
                .Select(entry => entry.Comment).FirstOrDefault()));

    private static async Task<Guid[]> PriceChangedCaseIdsAsync(LandErpDbContext db, IQueryable<Row> visible, CancellationToken cancellationToken)
    {
        IQueryable<Guid> visibleIds = visible.Select(row => row.Case.Id);
        RevisionCandidate[] candidates = await (from link in db.PropertyCaseSourceLinks.AsNoTracking()
                                                join source in db.Listings.AsNoTracking() on link.CatalogItemId equals source.Id
                                                where link.Confirmed && visibleIds.Contains(link.PropertyCaseId)
                                                    && source.DataRevision > link.ReviewedDataRevision
                                                select new RevisionCandidate(link.PropertyCaseId, source.Id, source.DataRevision, link.ReviewedDataRevision))
            .ToArrayAsync(cancellationToken);
        if (candidates.Length == 0) return [];

        Guid[] sourceIds = candidates.Select(item => item.CatalogItemId).Distinct().ToArray();
        CatalogEvent[] events = await db.CatalogEvents.AsNoTracking()
            .Where(item => sourceIds.Contains(item.CatalogItemId) && item.Kind == CatalogEventKind.SourceChanged)
            .OrderByDescending(item => item.RecordedAt).ThenByDescending(item => item.Id)
            .ToArrayAsync(cancellationToken);
        Dictionary<Guid, CatalogEvent[]> eventsBySource = events.GroupBy(item => item.CatalogItemId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        HashSet<Guid> changedCases = [];
        foreach (RevisionCandidate candidate in candidates)
        {
            long revisionDelta = candidate.DataRevision - candidate.ReviewedDataRevision;
            if (revisionDelta <= 0) continue;
            CatalogEvent[] sourceEvents = eventsBySource.GetValueOrDefault(candidate.CatalogItemId, []);
            long expectedSourceChangeEvents = Math.Max(0L, candidate.DataRevision - 1L);
            if (sourceEvents.LongLength != expectedSourceChangeEvents) continue;
            int take = revisionDelta > int.MaxValue ? int.MaxValue : (int)revisionDelta;
            if (sourceEvents.Take(take).Any(item => item.Message.Contains("цена", StringComparison.OrdinalIgnoreCase)))
                changedCases.Add(candidate.CaseId);
        }
        return changedCases.ToArray();
    }

    private static async Task<ProcurementQueueV2Summary> ReadSummaryAsync(LandErpDbContext db, IQueryable<Row> visible,
        Guid[] priceChangedCaseIds, DateTimeOffset todayStart, DateTimeOffset tomorrowStart, CancellationToken cancellationToken)
    {
        IQueryable<Row> active = visible.Where(row => row.Case.StageId != "rejected" && row.Case.StageId != "acquired");
        int inWork = await active.CountAsync(cancellationToken);
        int dueToday = await active.CountAsync(row => !row.Task.Completed && row.Task.DueAt >= todayStart && row.Task.DueAt < tomorrowStart, cancellationToken);
        int overdue = await active.CountAsync(row => !row.Task.Completed && row.Task.DueAt < todayStart, cancellationToken);
        int changed = await WhereSourceChanged(active, db).CountAsync(cancellationToken);
        int returned = await active.CountAsync(row => row.Case.StageId == "returned", cancellationToken);
        int checking = await active.CountAsync(row => row.Case.StageId == "analysis", cancellationToken);
        int pendingHead = await active.CountAsync(row => row.Case.StageId == "pending_head", cancellationToken);
        int priceChanged = priceChangedCaseIds.Length == 0
            ? 0
            : await active.CountAsync(row => priceChangedCaseIds.Contains(row.Case.Id), cancellationToken);
        return new(inWork, dueToday, overdue, changed, returned, checking, pendingHead, priceChanged);
    }
}
