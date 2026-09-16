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
        IQueryable<Row> rows = from item in db.PropertyCases.AsNoTracking()
                               join assignment in db.WorkAssignments.AsNoTracking() on item.AssignmentId equals assignment.Id
                               join task in db.WorkTasks.AsNoTracking() on item.WorkTaskId equals task.Id
                               where item.OrganizationId == context.OrganizationId
                               select new Row { Case = item, Assignment = assignment, Task = task };
        return context.Scope switch
        {
            AccessScope.Organization => rows,
            AccessScope.Department => rows.Where(row => context.DepartmentId != null && row.Case.DepartmentId == context.DepartmentId),
            AccessScope.Team => rows.Where(row => context.TeamId != null && row.Case.TeamId == context.TeamId),
            AccessScope.AssignedObjects => rows.Where(row => row.Assignment.EmployeeId == context.EmployeeId),
            _ => rows.Where(row => row.Case.ManagerEmployeeId == context.EmployeeId)
        };
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
            source.DataRevision, link.ReviewedDataRevision);

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

    private static async Task<ProcurementQueueV2Summary> ReadSummaryAsync(LandErpDbContext db, IQueryable<Row> visible,
        DateTimeOffset todayStart, DateTimeOffset tomorrowStart, CancellationToken cancellationToken)
    {
        IQueryable<Row> active = visible.Where(row => row.Case.StageId != "rejected" && row.Case.StageId != "acquired");
        int inWork = await active.CountAsync(cancellationToken);
        int dueToday = await active.CountAsync(row => !row.Task.Completed && row.Task.DueAt >= todayStart && row.Task.DueAt < tomorrowStart, cancellationToken);
        int overdue = await active.CountAsync(row => !row.Task.Completed && row.Task.DueAt < todayStart, cancellationToken);
        int changed = await WhereSourceChanged(active, db).CountAsync(cancellationToken);
        int returned = await active.CountAsync(row => row.Case.StageId == "returned", cancellationToken);
        return new(inWork, dueToday, overdue, changed, returned);
    }
}
