using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Application.Modules.Workflow.Domain;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Modules.Procurement;

/// <summary>Case-centric read projection for the approved procurement queue V2 UI.</summary>
public sealed partial class ProcurementQueueV2ReadService(
    IDbContextFactory<LandErpDbContext> factory,
    IAccessControl access,
    TimeProvider clock) : IProcurementQueueV2ReadService
{
    private readonly IDbContextFactory<LandErpDbContext> _factory = factory;
    private readonly IAccessControl _access = access;
    private readonly TimeProvider _clock = clock;

    private sealed class Row
    {
        public PropertyCase Case { get; init; } = default!;
        public Assignment Assignment { get; init; } = default!;
        public WorkTask Task { get; init; } = default!;
    }

    private sealed record SourceDb(Guid CaseId, Guid CatalogItemId, CatalogSource Source, string? ExternalId, string? Url,
        string? Title, decimal? Price, string Currency, decimal? AreaSquareMeters, string? Location, string? CadastralNumber,
        string PhotosJson, string Provenance, DateTimeOffset? LastObservedAt, DateTimeOffset ChangedAt,
        long DataRevision, long ReviewedDataRevision);
    private sealed record CheckDb(Guid CaseId, CaseCheckLevel Level, CaseCheckStatus Status, bool Blocker, Guid? ResponsibleEmployeeId);
    private sealed record ContactDb(Guid CaseId, DateTimeOffset? EffectiveAt, string? Channel, string? Outcome, string? Comment);
    private sealed record InspectionItemDb(InspectionItemStatus Status, string Answer, string NormalAnswer);

    public async Task<ProcurementQueueV2Page> ReadPageAsync(Subject subject, ProcurementQueueV2Filter filter, CancellationToken cancellationToken)
    {
        AccessContext context = await _access.RequireAsync(subject, Permissions.QueueRead, cancellationToken);
        await using LandErpDbContext db = await _factory.CreateDbContextAsync(cancellationToken);
        (DateTimeOffset todayStart, DateTimeOffset tomorrowStart) = TodayBounds();
        int offset = Math.Max(0, filter.Offset);
        int size = Math.Clamp(filter.Size, 1, 100);
        IQueryable<Row> visible = VisibleCases(db, context);
        IQueryable<Row> query = string.IsNullOrWhiteSpace(filter.Stage)
            ? visible.Where(row => row.Case.StageId != "rejected" && row.Case.StageId != "acquired")
            : visible.Where(row => row.Case.StageId == filter.Stage);

        string text = filter.Text.Trim();
        if (text.Length > 0)
        {
            string pattern = $"%{text}%";
            query = query.Where(row => EF.Functions.ILike(row.Case.BusinessNumber, pattern)
                || EF.Functions.ILike(row.Case.WorkingTitle, pattern)
                || (row.Case.WorkingLocation != null && EF.Functions.ILike(row.Case.WorkingLocation, pattern))
                || (row.Case.CadastralNumber != null && EF.Functions.ILike(row.Case.CadastralNumber, pattern))
                || db.PropertyCaseSourceLinks.Any(link => link.PropertyCaseId == row.Case.Id && link.Confirmed
                    && db.Listings.Any(source => source.Id == link.CatalogItemId
                        && ((source.ExternalId != null && EF.Functions.ILike(source.ExternalId, pattern))
                            || (source.Title != null && EF.Functions.ILike(source.Title, pattern))))));
        }
        if (filter.AssigneeId is Guid assigneeId) query = query.Where(row => row.Assignment.EmployeeId == assigneeId);
        if (filter.Source is CatalogSource source)
            query = query.Where(row => db.PropertyCaseSourceLinks.Any(link => link.PropertyCaseId == row.Case.Id && link.Confirmed
                && db.Listings.Any(item => item.Id == link.CatalogItemId && item.Source == source)));
        if (filter.SourceChangedOnly) query = WhereSourceChanged(query, db);
        if (filter.DueTodayOnly) query = query.Where(row => !row.Task.Completed && row.Task.DueAt >= todayStart && row.Task.DueAt < tomorrowStart);
        if (filter.OverdueOnly) query = query.Where(row => !row.Task.Completed && row.Task.DueAt < todayStart);
        query = ApplyCheckFilter(query, filter.Checks, db);

        int total = await query.CountAsync(cancellationToken);
        Row[] pageRows = await ApplySort(query, filter, db).Skip(offset).Take(size).ToArrayAsync(cancellationToken);
        Guid[] pageIds = pageRows.Select(row => row.Case.Id).ToArray();
        SourceDb[] sourceRows = pageIds.Length == 0 ? [] : await SourceRows(db, context.OrganizationId, pageIds).ToArrayAsync(cancellationToken);
        CheckDb[] checkRows = pageIds.Length == 0 ? [] : await db.CaseChecks.AsNoTracking()
            .Where(item => pageIds.Contains(item.PropertyCaseId) && item.Level == CaseCheckLevel.Quick)
            .Select(item => new CheckDb(item.PropertyCaseId, item.Level, item.Status, item.Blocker, item.ResponsibleEmployeeId)).ToArrayAsync(cancellationToken);
        ContactDb[] contacts = pageIds.Length == 0 ? [] : await LatestContacts(db, pageIds).ToArrayAsync(cancellationToken);

        Guid[] assigneeIds = await visible.Where(row => row.Case.StageId != "rejected" && row.Case.StageId != "acquired")
            .Select(row => row.Assignment.EmployeeId).Distinct().ToArrayAsync(cancellationToken);
        ProcurementQueueV2Assignee[] assignees = await db.Employees.AsNoTracking()
            .Where(item => item.OrganizationId == context.OrganizationId && assigneeIds.Contains(item.Id))
            .OrderBy(item => item.DisplayName).Select(item => new ProcurementQueueV2Assignee(item.Id, item.DisplayName)).ToArrayAsync(cancellationToken);
        Dictionary<Guid, string> names = assignees.ToDictionary(item => item.Id, item => item.Name);
        string[] stages = await visible.Select(row => row.Case.StageId).Distinct().OrderBy(item => item).ToArrayAsync(cancellationToken);
        ProcurementQueueV2Summary summary = await ReadSummaryAsync(db, visible, todayStart, tomorrowStart, cancellationToken);
        Dictionary<Guid, SourceDb[]> sourcesByCase = sourceRows.GroupBy(item => item.CaseId).ToDictionary(group => group.Key, group => group.ToArray());
        Dictionary<Guid, CheckDb[]> checksByCase = checkRows.GroupBy(item => item.CaseId).ToDictionary(group => group.Key, group => group.ToArray());
        Dictionary<Guid, ContactDb> contactsByCase = contacts.Where(item => item.EffectiveAt != null).ToDictionary(item => item.CaseId);

        ProcurementQueueV2Row[] items = pageRows.Select(row => ProjectRow(row, names, sourcesByCase.GetValueOrDefault(row.Case.Id, []),
            checksByCase.GetValueOrDefault(row.Case.Id, []), contactsByCase.GetValueOrDefault(row.Case.Id), todayStart, tomorrowStart)).ToArray();
        return new(items, total, summary, assignees, stages, offset, size);
    }

    public async Task<ProcurementQueueV2Detail> ReadDetailAsync(Subject subject, Guid caseId, CancellationToken cancellationToken)
    {
        AccessContext context = await _access.RequireAsync(subject, Permissions.QueueRead, cancellationToken);
        await using LandErpDbContext db = await _factory.CreateDbContextAsync(cancellationToken);
        Row? row = await VisibleCases(db, context).SingleOrDefaultAsync(item => item.Case.Id == caseId, cancellationToken);
        if (row == null) throw new AccessDeniedException();
        (DateTimeOffset todayStart, DateTimeOffset tomorrowStart) = TodayBounds();
        SourceDb[] sourceRows = (await SourceRows(db, context.OrganizationId, [caseId]).ToArrayAsync(cancellationToken))
            .OrderByDescending(item => item.LastObservedAt ?? item.ChangedAt).ToArray();
        bool sourceChanged = sourceRows.Any(item => item.DataRevision > item.ReviewedDataRevision);

        ProcurementQueueV2Negotiation[] negotiations = await db.CaseNegotiations.AsNoTracking().Where(item => item.PropertyCaseId == caseId)
            .OrderByDescending(item => item.EffectiveAt).ThenByDescending(item => item.RecordedAt).Take(3)
            .Select(item => new ProcurementQueueV2Negotiation(item.Id, item.EffectiveAt, item.Channel, item.Outcome, item.Comment,
                item.SellerPrice, item.BuyerOffer, item.AgreedPrice, item.Currency)).ToArrayAsync(cancellationToken);
        decimal? sellerOffer = await LatestPrice(db.CaseNegotiations.Where(item => item.PropertyCaseId == caseId && item.SellerPrice != null), 0, cancellationToken);
        decimal? buyerOffer = await LatestPrice(db.CaseNegotiations.Where(item => item.PropertyCaseId == caseId && item.BuyerOffer != null), 1, cancellationToken);
        decimal? agreedPrice = await LatestPrice(db.CaseNegotiations.Where(item => item.PropertyCaseId == caseId && item.AgreedPrice != null), 2, cancellationToken);

        CheckDb[] checks = await db.CaseChecks.AsNoTracking().Where(item => item.PropertyCaseId == caseId)
            .Select(item => new CheckDb(item.PropertyCaseId, item.Level, item.Status, item.Blocker, item.ResponsibleEmployeeId)).ToArrayAsync(cancellationToken);
        Guid[] peopleIds = checks.Where(item => item.ResponsibleEmployeeId != null).Select(item => item.ResponsibleEmployeeId!.Value)
            .Append(row.Assignment.EmployeeId).Distinct().ToArray();
        Dictionary<Guid, string> people = await db.Employees.AsNoTracking().Where(item => item.OrganizationId == context.OrganizationId && peopleIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.DisplayName, cancellationToken);
        ProcurementCheckLevelSummary quick = CheckLevelSummary(CaseCheckLevel.Quick, checks, people);
        ProcurementCheckLevelSummary deep = CheckLevelSummary(CaseCheckLevel.Deep, checks, people);
        ProcurementInspectionSummary inspection = await ReadInspectionAsync(db, caseId, cancellationToken);
        ProcurementTimelineSummary[] timeline = await ReadTimelineAsync(db, context.OrganizationId, caseId, cancellationToken);
        ProcurementSourceDetail[] sources = sourceRows.Select(source => SourceDetail(row.Case, source)).ToArray();
        SourceDb? askSource = sourceRows.FirstOrDefault(item => item.Price != null);
        bool managerPermission = await AllowedAsync(subject, Permissions.ManagerDecide, cancellationToken);
        bool headPermission = await AllowedAsync(subject, Permissions.HeadDecide, cancellationToken);
        bool canManagerDecide = managerPermission && row.Assignment.EmployeeId == context.EmployeeId
            && row.Case.StageId != "pending_head" && (row.Case.StageId != "rejected" || sourceChanged);
        bool canHeadDecide = headPermission && row.Case.StageId == "pending_head"
            && row.Assignment.EmployeeId == context.EmployeeId && row.Case.ManagerEmployeeId != context.EmployeeId;

        return new(row.Case.Id, row.Case.BusinessNumber, row.Case.WorkingTitle, row.Case.WorkingLocation, row.Case.CadastralNumber,
            row.Case.WorkingAreaSquareMeters, row.Case.WorkingPrice, row.Case.Currency, row.Case.StageId, row.Assignment.EmployeeId,
            people.GetValueOrDefault(row.Assignment.EmployeeId, "Сотрудник"), FirstPhoto(sourceRows), askSource?.Price,
            askSource == null ? null : SourceLabel(askSource), sellerOffer, buyerOffer, agreedPrice, row.Task.Title, row.Task.DueAt,
            DueState(row.Task, todayStart, tomorrowStart), negotiations, quick, deep, inspection, timeline, sources, sourceChanged,
            sourceRows.Length == 0 ? 0 : sourceRows.Max(item => item.DataRevision), row.Case.Version, canManagerDecide, canHeadDecide,
            managerPermission || headPermission);
    }
}
