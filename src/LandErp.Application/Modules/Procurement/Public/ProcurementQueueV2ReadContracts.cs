using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Procurement.Domain;

namespace LandErp.Application.Modules.Procurement.Contracts;

public enum ProcurementQueueV2CheckFilter { Any, HasIssues, Incomplete, Complete }
public enum ProcurementQueueV2Sort { RecordedAt, DueAt, WorkingPrice, Area, LastContact }
public enum ProcurementDueState { None, Normal, Today, Overdue }
public enum ProcurementQueueV2RowState { Normal, SourceChanged, CheckIssue, DueToday, Overdue }

public sealed record ProcurementQueueV2Filter(
    string Text = "",
    string Stage = "",
    Guid? AssigneeId = null,
    CatalogSource? Source = null,
    bool SourceChangedOnly = false,
    bool DueTodayOnly = false,
    bool OverdueOnly = false,
    ProcurementQueueV2CheckFilter Checks = ProcurementQueueV2CheckFilter.Any,
    ProcurementQueueV2Sort Sort = ProcurementQueueV2Sort.RecordedAt,
    bool Descending = true,
    int Offset = 0,
    int Size = 30,
    bool MineOnly = false);

public sealed record ProcurementQueueV2Summary(int InWork, int DueToday, int Overdue, int SourceChanged, int Returned);
public sealed record ProcurementQueueV2Assignee(Guid Id, string Name);
public sealed record ProcurementQuickCheckSummary(int Total, int Completed, int Issues, int Remaining);
public sealed record ProcurementLatestContact(DateTimeOffset EffectiveAt, string Channel, string Outcome, string Comment);
public sealed record ProcurementSourceTypeSummary(CatalogSource Source, int Count);

public sealed record ProcurementQueueV2Row(
    Guid CaseId,
    string BusinessNumber,
    string Title,
    string? Location,
    string? CadastralNumber,
    string? ThumbnailUrl,
    string Stage,
    decimal? WorkingPrice,
    string Currency,
    decimal? AreaSquareMeters,
    string NextActionTitle,
    DateTimeOffset? DueAt,
    ProcurementDueState DueState,
    Guid AssigneeId,
    string AssigneeName,
    ProcurementQuickCheckSummary QuickChecks,
    ProcurementLatestContact? LatestContact,
    IReadOnlyList<ProcurementSourceTypeSummary> Sources,
    int SourceCount,
    bool SourceChanged,
    ProcurementQueueV2RowState RowState,
    long CaseVersion,
    long SourceRevision);

public sealed record ProcurementQueueV2Page(
    IReadOnlyList<ProcurementQueueV2Row> Items,
    int Total,
    ProcurementQueueV2Summary Summary,
    IReadOnlyList<ProcurementQueueV2Assignee> Assignees,
    IReadOnlyList<string> Stages,
    int Offset,
    int Size);

public sealed record ProcurementQueueV2Negotiation(
    Guid Id,
    DateTimeOffset EffectiveAt,
    string Channel,
    string Outcome,
    string Comment,
    decimal? SellerPrice,
    decimal? BuyerOffer,
    decimal? AgreedPrice,
    string Currency);

public sealed record ProcurementCheckLevelSummary(
    CaseCheckLevel Level,
    int Total,
    int Completed,
    int Issues,
    int Remaining,
    IReadOnlyList<string> Responsible);

public sealed record ProcurementInspectionSummary(
    bool Exists,
    Guid? InspectionId,
    InspectionStatus? Status,
    DateTimeOffset? Date,
    string Conclusion,
    int TotalItems,
    int CheckedItems,
    int Problems,
    int MaterialCount);

public sealed record ProcurementTimelineSummary(
    Guid Id,
    string Kind,
    string Title,
    string Body,
    string Actor,
    DateTimeOffset RecordedAt,
    DateTimeOffset? EffectiveAt,
    DateTimeOffset? DueAt);

public sealed record ProcurementSourceDetail(
    Guid CatalogItemId,
    CatalogSource Source,
    string? ExternalId,
    string? Url,
    string? Title,
    decimal? Price,
    string Currency,
    decimal? AreaSquareMeters,
    string? Location,
    string? CadastralNumber,
    string Provenance,
    DateTimeOffset? LastObservedAt,
    bool Changed,
    IReadOnlyList<CaseFactField> ApplicableFacts);

public sealed record ProcurementQueueV2Detail(
    Guid CaseId,
    string BusinessNumber,
    string Title,
    string? Location,
    string? CadastralNumber,
    decimal? AreaSquareMeters,
    decimal? WorkingPrice,
    string Currency,
    string Stage,
    Guid AssigneeId,
    string AssigneeName,
    string? ThumbnailUrl,
    decimal? CurrentSourceAsk,
    string? CurrentSourceLabel,
    decimal? LatestSellerOffer,
    decimal? LatestBuyerOffer,
    decimal? LatestAgreedPrice,
    string NextActionTitle,
    DateTimeOffset? DueAt,
    ProcurementDueState DueState,
    IReadOnlyList<ProcurementQueueV2Negotiation> Negotiations,
    ProcurementCheckLevelSummary QuickChecks,
    ProcurementCheckLevelSummary DeepChecks,
    ProcurementInspectionSummary Inspection,
    IReadOnlyList<ProcurementTimelineSummary> Timeline,
    IReadOnlyList<ProcurementSourceDetail> Sources,
    bool SourceChanged,
    long SourceRevision,
    long CaseVersion,
    bool CanManagerDecide,
    bool CanHeadDecide,
    bool CanManageDossier);

public interface IProcurementQueueV2ReadService
{
    Task<ProcurementQueueV2Page> ReadPageAsync(Subject subject, ProcurementQueueV2Filter filter, CancellationToken cancellationToken);
    Task<ProcurementQueueV2Detail> ReadDetailAsync(Subject subject, Guid caseId, CancellationToken cancellationToken);
}
