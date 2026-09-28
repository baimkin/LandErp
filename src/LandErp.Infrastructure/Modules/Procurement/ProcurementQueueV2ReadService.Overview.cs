using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Application.Modules.Workflow.Domain;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Modules.Procurement;

public sealed partial class ProcurementQueueV2ReadService
{
    // Count and destination use the very same authorized rows, including the one
    // selected next task per case. Several sources/reasons never multiply a case.
    internal IQueryable<PropertyCase> OverviewCases(LandErpDbContext db, EffectiveEmployeeAccess access, bool attention = false)
    {
        var rows=VisibleReadCases(db,access).Where(row=>row.Case.StageId!="rejected" && row.Case.StageId!="acquired");
        return (attention ? WhereAttention(rows,db) : rows).Select(row=>row.Case);
    }

    private IQueryable<Row> WhereAttention(IQueryable<Row> rows, LandErpDbContext db)
    {
        var now=_clock.GetUtcNow();var day=WorkTaskDeadline.DayStart(now);var stalled=now.AddDays(-3);
        return rows.Where(row=>row.Case.StageId!="rejected" && row.Case.StageId!="acquired")
            .Where(row=>row.Case.StageId=="returned"
                || !row.Task.Completed && !row.Task.Deleted && row.Task.DueAt < (row.Task.DueHasTime ? now : day)
                || !row.Task.Completed && !row.Task.Deleted && row.Task.DueAt==null && row.Task.RecordedAt<stalled
                || db.CaseChecks.Any(check=>check.PropertyCaseId==row.Case.Id && (check.Status==CaseCheckStatus.Issue || check.Status==CaseCheckStatus.Blocked || check.Blocker))
                || db.PropertyCaseSourceLinks.Any(link=>link.PropertyCaseId==row.Case.Id && link.Confirmed
                    && db.Listings.Any(source=>source.Id==link.CatalogItemId && source.DataRevision>link.ReviewedDataRevision)));
    }
}
