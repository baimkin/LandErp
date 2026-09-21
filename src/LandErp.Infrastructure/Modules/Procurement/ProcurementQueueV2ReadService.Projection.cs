using System.Text.Json;
using LandErp.Application.Foundation;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Application.Modules.Workflow.Domain;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Modules.Procurement;

public sealed partial class ProcurementQueueV2ReadService
{
    private static ProcurementQueueV2Row ProjectRow(Row row, Dictionary<Guid, string> names, SourceDb[] sources, CheckDb[] checks,
        ContactDb? contact, DateTimeOffset todayStart, DateTimeOffset tomorrowStart)
    {
        ProcurementQuickCheckSummary quick = QuickSummary(checks);
        bool changed = sources.Any(item => item.DataRevision > item.ReviewedDataRevision);
        ProcurementDueState due = DueState(row.Task, todayStart, tomorrowStart);
        ProcurementLatestContact? latest = contact?.EffectiveAt == null ? null
            : new ProcurementLatestContact(contact.EffectiveAt.Value, contact.Channel ?? "", contact.Outcome ?? "", contact.Comment ?? "");
        ProcurementSourceTypeSummary[] sourceTypes = sources.GroupBy(item => item.Source).OrderBy(group => group.Key)
            .Select(group => new ProcurementSourceTypeSummary(group.Key, group.Count())).ToArray();
        return new(row.Case.Id, row.Case.BusinessNumber, row.Case.WorkingTitle, row.Case.WorkingLocation, row.Case.CadastralNumber, FirstPhoto(sources),
            row.Case.StageId, row.Case.WorkingPrice, row.Case.Currency, row.Case.WorkingAreaSquareMeters, row.Task.Type, row.Task.Title,
            row.Task.Description, row.Task.DueAt, due, row.Task.EmployeeId, names.GetValueOrDefault(row.Task.EmployeeId, "Сотрудник"), quick, latest, sourceTypes, sources.Length, changed,
            RowState(due, quick, changed), row.Case.Version, sources.Length == 0 ? 0 : sources.Max(item => item.DataRevision));
    }

    private static async Task<decimal?> LatestPrice(IQueryable<CaseNegotiation> query, int field, CancellationToken cancellationToken)
    {
        IOrderedQueryable<CaseNegotiation> ordered = query.AsNoTracking().OrderByDescending(item => item.EffectiveAt).ThenByDescending(item => item.RecordedAt);
        return field switch
        {
            0 => await ordered.Select(item => item.SellerPrice).FirstOrDefaultAsync(cancellationToken),
            1 => await ordered.Select(item => item.BuyerOffer).FirstOrDefaultAsync(cancellationToken),
            _ => await ordered.Select(item => item.AgreedPrice).FirstOrDefaultAsync(cancellationToken)
        };
    }

    private static async Task<ProcurementInspectionSummary> ReadInspectionAsync(LandErpDbContext db, Guid caseId, CancellationToken cancellationToken)
    {
        SiteInspection? inspection = await db.SiteInspections.AsNoTracking().Where(item => item.PropertyCaseId == caseId)
            .OrderByDescending(item => item.StartedAt).FirstOrDefaultAsync(cancellationToken);
        if (inspection == null) return new(false, null, null, null, "", 0, 0, 0, 0, 0, 0, 0);
        InspectionItemDb[] items = await db.SiteInspectionItems.AsNoTracking().Where(item => item.InspectionId == inspection.Id)
            .Select(item => new InspectionItemDb(item.Status, item.Answer, item.NormalAnswerSnapshot)).ToArrayAsync(cancellationToken);
        CaseAttachmentKind[] materialKinds = await db.CaseAttachments.AsNoTracking().Where(item => item.PropertyCaseId == caseId
            && (item.OwnerType == CaseAttachmentOwner.Inspection || item.OwnerType == CaseAttachmentOwner.InspectionItem))
            .Select(item => item.Kind).ToArrayAsync(cancellationToken);
        int checkedItems = items.Count(item => item.Status != InspectionItemStatus.Unanswered);
        int problems = items.Count(item => item.Status == InspectionItemStatus.Answered && item.NormalAnswer.Length > 0
            && !string.Equals(item.Answer, item.NormalAnswer, StringComparison.OrdinalIgnoreCase));
        return new(true, inspection.Id, inspection.Status, inspection.CompletedAt ?? inspection.StartedAt, inspection.OverallConclusion,
            items.Length, checkedItems, problems, materialKinds.Length,
            materialKinds.Count(kind => kind is CaseAttachmentKind.Photo or CaseAttachmentKind.Video),
            materialKinds.Count(kind => kind == CaseAttachmentKind.Audio),
            materialKinds.Count(kind => kind is CaseAttachmentKind.Document or CaseAttachmentKind.Link));
    }

    private static async Task<ProcurementTimelineSummary[]> ReadTimelineAsync(LandErpDbContext db, Guid organizationId, Guid caseId, CancellationToken cancellationToken)
    {
        BusinessTimelineEntry[] entries = await db.BusinessTimeline.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && item.ObjectType == "PropertyCase" && item.ObjectId == caseId)
            .OrderByDescending(item => item.RecordedAt).Take(6).ToArrayAsync(cancellationToken);
        Guid[] actorIds = entries.Select(item => item.ActorEmployeeId).Distinct().ToArray();
        Dictionary<Guid, string> actors = await db.Employees.AsNoTracking().Where(item => item.OrganizationId == organizationId && actorIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.DisplayName, cancellationToken);
        return entries.Select(item => new ProcurementTimelineSummary(item.Id, item.Kind, item.Title, item.Body,
            actors.GetValueOrDefault(item.ActorEmployeeId, "Сотрудник"), item.RecordedAt, item.EffectiveAt, item.DueAt)).ToArray();
    }

    private static ProcurementQuickCheckSummary QuickSummary(CheckDb[] rows)
    {
        int completed = rows.Count(item => item.Status == CaseCheckStatus.Passed);
        int issues = rows.Count(item => (item.Status is CaseCheckStatus.Issue or CaseCheckStatus.Blocked) || item.Blocker);
        return new(rows.Length, completed, issues, Math.Max(0, rows.Length - completed - issues));
    }

    private static ProcurementCheckLevelSummary CheckLevelSummary(CaseCheckLevel level, CheckDb[] rows, Dictionary<Guid, string> people)
    {
        CheckDb[] items = rows.Where(item => item.Level == level).ToArray();
        int completed = items.Count(item => item.Status == CaseCheckStatus.Passed);
        int issues = items.Count(item => (item.Status is CaseCheckStatus.Issue or CaseCheckStatus.Blocked) || item.Blocker);
        string[] responsible = items.Where(item => item.ResponsibleEmployeeId != null)
            .Select(item => people.GetValueOrDefault(item.ResponsibleEmployeeId!.Value, "Сотрудник"))
            .Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToArray();
        return new(level, items.Length, completed, issues, Math.Max(0, items.Length - completed - issues), responsible);
    }

    private static ProcurementSourceDetail SourceDetail(PropertyCase propertyCase, SourceDb source) => new(source.CatalogItemId, source.Source,
        source.ExternalId, source.Url, source.Title, source.Price, source.Currency, source.AreaSquareMeters, source.Location, source.CadastralNumber,
        source.Provenance, source.LastObservedAt, source.DataRevision > source.ReviewedDataRevision, ApplicableFacts(propertyCase, source));

    private static CaseFactField[] ApplicableFacts(PropertyCase propertyCase, SourceDb source)
    {
        List<CaseFactField> fields = [];
        if (!string.IsNullOrWhiteSpace(source.Title) && !string.Equals(source.Title, propertyCase.WorkingTitle, StringComparison.Ordinal)) fields.Add(CaseFactField.Title);
        if (source.Price != null && source.Price != propertyCase.WorkingPrice) fields.Add(CaseFactField.Price);
        if (source.AreaSquareMeters != null && source.AreaSquareMeters != propertyCase.WorkingAreaSquareMeters) fields.Add(CaseFactField.AreaSquareMeters);
        if (!string.IsNullOrWhiteSpace(source.Location) && !string.Equals(source.Location, propertyCase.WorkingLocation, StringComparison.Ordinal)) fields.Add(CaseFactField.Location);
        if (!string.IsNullOrWhiteSpace(source.CadastralNumber) && !string.Equals(source.CadastralNumber, propertyCase.CadastralNumber, StringComparison.Ordinal)) fields.Add(CaseFactField.CadastralNumber);
        return fields.ToArray();
    }

    private static ProcurementDueState DueState(WorkTask task, DateTimeOffset todayStart, DateTimeOffset tomorrowStart)
    {
        if (task.Completed || task.DueAt == null) return ProcurementDueState.None;
        if (task.DueAt < todayStart) return ProcurementDueState.Overdue;
        return task.DueAt < tomorrowStart ? ProcurementDueState.Today : ProcurementDueState.Normal;
    }

    private static ProcurementQueueV2RowState RowState(ProcurementDueState due, ProcurementQuickCheckSummary checks, bool changed) => due switch
    {
        ProcurementDueState.Overdue => ProcurementQueueV2RowState.Overdue,
        ProcurementDueState.Today => ProcurementQueueV2RowState.DueToday,
        _ when checks.Issues > 0 => ProcurementQueueV2RowState.CheckIssue,
        _ when changed => ProcurementQueueV2RowState.SourceChanged,
        _ => ProcurementQueueV2RowState.Normal
    };

    private (DateTimeOffset Start, DateTimeOffset End) TodayBounds()
    {
        TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(DataConventions.BusinessTimeZoneId);
        DateTimeOffset localNow = TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), zone);
        DateTime localStart = new(localNow.Year, localNow.Month, localNow.Day, 0, 0, 0, DateTimeKind.Unspecified);
        return (new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localStart, zone)),
            new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localStart.AddDays(1), zone)));
    }

    private static string? FirstPhoto(SourceDb[] sources) => sources.OrderByDescending(item => item.LastObservedAt ?? item.ChangedAt)
        .SelectMany(item => PhotoUrls(item.PhotosJson)).FirstOrDefault();
    private static string SourceLabel(SourceDb source) => string.IsNullOrWhiteSpace(source.ExternalId) ? source.Source.ToString() : $"{source.Source} · {source.ExternalId}";
    private static string[] PhotoUrls(string json)
    {
        try { return JsonSerializer.Deserialize<string[]>(json) ?? []; }
        catch (JsonException) { return []; }
    }
}
