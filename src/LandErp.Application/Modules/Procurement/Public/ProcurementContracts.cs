using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Foundation.Files;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Application.Modules.Workflow.Domain;
using LandErp.Collector.Contracts.V1;

namespace LandErp.Application.Modules.Procurement.Contracts;

public enum ProcurementAction { Monitor, Clarify, Reject, Forward, Return, Approve }
public sealed record QueueFilter(string Text = "", string Stage = "", CatalogSource? Source = null, bool ChangedOnly = false, int Offset = 0, int Size = 40);
public sealed record QueueItem(Guid CaseId, string BusinessNumber, string Title, CatalogSource[] Sources,
    decimal? Price, string Currency, decimal? Area, string? Location, string Stage, string? Assignee, DateTimeOffset? DueAt,
    string Reason, string[] UnknownFields, bool Changed, long SourceRevision, long CaseVersion,
    string NextActionTitle, string NextActionDescription, long NextActionVersion);
public sealed record ProcurementQueuePage(IReadOnlyList<QueueItem> Items, int Total);
public sealed record TimelineItem(Guid Id, string Kind, string Title, string Body, string Actor, string? Target,
    DateTimeOffset RecordedAt, DateTimeOffset? EffectiveAt, DateTimeOffset? DueAt);
public sealed record ObservationView(Guid Id, Guid CatalogItemId, DateTimeOffset ObservedAt, DateTimeOffset RecordedAt, ListingData Data, string[] Changes);
public sealed record CaseSourceView(Guid CatalogItemId, CatalogSource Source, string? ExternalId, string? Url, string Title,
    decimal? Price, decimal? AreaSquareMeters, string? Location, string Provenance, DateTimeOffset? LastObservedAt);
public sealed record DecisionTarget(Guid EmployeeId, string Name);
public sealed record NegotiationView(Guid Id, decimal? SellerPrice, decimal? BuyerOffer, decimal? AgreedPrice, string Currency,
    string Channel, string Contact, string Outcome, string Conditions, string Comment, string NextStep, DateTimeOffset? NextStepDueAt,
    string Author, DateTimeOffset EffectiveAt, DateTimeOffset RecordedAt);
public sealed record CheckView(Guid Id, CaseCheckLevel Level, string Title, CaseCheckStatus Status,
    Guid? ResponsibleEmployeeId, string? Responsible, DateTimeOffset? DueAt, decimal? Cost, string Currency, string Result,
    bool Blocker, long Version, string DescriptionSnapshot, Guid? TemplateItemId, long? TemplateItemVersion);
public sealed record AttachmentView(Guid Id, CaseAttachmentOwner OwnerType, Guid? OwnerId, CaseAttachmentKind Kind,
    string Label, string Description, string OwnerLabel, string OriginalName, string ContentType, long SizeBytes, StoredFileStatus Status,
    bool External, DateTimeOffset RecordedAt, Guid? DocumentRequirementId);
public sealed record DocumentRequirementView(Guid Id, string Code, string Title, string Description, string ExpectedSource,
    CaseDocumentStatus Status, DateTimeOffset? DueAt, string Note, string UpdatedBy, DateTimeOffset UpdatedAt, long Version,
    IReadOnlyList<Guid> AttachmentIds);
public sealed record CheckTemplateView(Guid Id, string Title, CaseCheckLevel Level, string Description, int SortOrder, bool Active, long Version);
public sealed record InspectionTemplateView(Guid Id, string Key, string Title, int SortOrder, InspectionAnswerType AnswerType,
    string[] Options, string Unit, string NormalAnswer, bool AllowAttachments, bool Required, bool Active, long Version);
public sealed record InspectionItemView(Guid Id, Guid TemplateItemId, long TemplateItemVersion, string Title, int SortOrder,
    InspectionAnswerType AnswerType, string[] Options, string Unit, string NormalAnswer, bool AllowAttachments, bool Required,
    InspectionItemStatus Status, string Answer, string Note, long Version);
public sealed record InspectionView(Guid Id, InspectionStatus Status, string OverallConclusion, string PreliminaryDecision,
    string Inspector, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, long Version, IReadOnlyList<InspectionItemView> Items);
public enum InspectionTaskState { Assigned, InProgress, Completed }
public sealed record InspectionTaskItem(Guid CaseId, string BusinessNumber, string Title, string? Location, string? CadastralNumber,
    string? RequestedBy, DateTimeOffset? RequestedAt, DateTimeOffset? DueAt, InspectionTaskState State, int DoneItems, int TotalItems);
public sealed record InspectionTaskPage(IReadOnlyList<InspectionTaskItem> Items, string BusinessTimeZone);
public sealed record InspectionAssignmentView(Guid InspectorEmployeeId, string Inspector, string? RequestedBy,
    DateTimeOffset? RequestedAt, DateTimeOffset? DueAt, string Instructions);
public sealed record InspectionWorkspaceView(Guid CaseId, string BusinessNumber, string Title, string? Location, string? CadastralNumber,
    string[] Photos, InspectionAssignmentView? Assignment, InspectionView? Inspection, IReadOnlyList<AttachmentView> Attachments,
    IReadOnlyList<DecisionTarget> Inspectors, bool CanAssign, bool CanPerform, bool ReturnToProcurement);
public sealed record SourceDiscrepancyView(Guid CatalogItemId, CatalogSource Source, CaseFactField Field,
    string WorkingValue, string SourceValue, bool Different);
public sealed record CaseCard(QueueItem Item, string? Description, string? SellerName, string[] Photos,
    IReadOnlyList<CaseSourceView> Sources, IReadOnlyList<TimelineItem> Timeline, IReadOnlyList<ObservationView> Observations,
    IReadOnlyList<DecisionTarget> Heads, IReadOnlyList<DecisionTarget> Managers, IReadOnlyList<DecisionTarget> Assignees,
    Guid ManagerEmployeeId, bool CanManagerDecide, bool CanHeadDecide,
    IReadOnlyList<NegotiationView> Negotiations, IReadOnlyList<CheckView> Checks, IReadOnlyList<AttachmentView> Attachments,
    IReadOnlyList<DocumentRequirementView> DocumentRequirements,
    IReadOnlyList<SourceDiscrepancyView> Discrepancies, IReadOnlyList<CheckTemplateView> CheckTemplates, InspectionView? Inspection,
    string? CadastralNumber, decimal? AcquisitionPrice, DateOnly? AcquisitionDate, string? AcquisitionComment,
    bool CanManageDossier, bool CanManageTemplates, bool CanManageBlockers, bool CanConfirmPurchase,
    bool CanCorrectSourceLinks)
{
    public bool CanAssignInspections { get; init; }
    public bool CanPerformInspections { get; init; }
}
public sealed record DecisionCommand(Guid CaseId, long ExpectedCaseVersion, long ExpectedSourceRevision,
    ProcurementAction Action, string Reason, string Clarification, Guid? TargetEmployeeId, DateTimeOffset? DueAt);
// Reuse CommandId for retries of the same addition. It is distinct from the per-attempt
// diagnostic correlation ID. Null retains legacy behavior without replay protection.
public sealed record AddCaseNote(Guid CaseId, long ExpectedCaseVersion, string Text, bool Contact, string ContactResult,
    DateTimeOffset? EffectiveAt, Guid? CommandId = null);
public sealed record AddNegotiation(Guid CaseId, long ExpectedCaseVersion, decimal? SellerPrice, decimal? BuyerOffer,
    decimal? AgreedPrice, string Channel, string Contact, string Outcome, string Conditions, string Comment,
    string NextStep, DateTimeOffset? NextStepDueAt, DateTimeOffset EffectiveAt, Guid? CommandId = null);
public sealed record SaveCaseCheck(Guid CaseId, Guid? CheckId, long ExpectedCaseVersion, long? ExpectedCheckVersion,
    CaseCheckLevel Level, string Title, CaseCheckStatus Status, Guid? ResponsibleEmployeeId, bool UpdateResponsible,
    DateTimeOffset? DueAt, decimal? Cost, string Result, bool Blocker, Guid? TemplateItemId = null);
public sealed record SaveCheckTemplate(Guid? Id, long? ExpectedVersion, string Title, CaseCheckLevel Level,
    string Description, int SortOrder, bool Active);
public sealed record AddCaseAttachment(Guid CaseId, CaseAttachmentOwner OwnerType, Guid? OwnerId,
    CaseAttachmentKind Kind, string Label, string Description, string OriginalName, string ContentType, byte[]? Content, string? ExternalUrl,
    Guid? DocumentRequirementId = null);
public sealed record SaveDocumentRequirement(Guid CaseId, Guid RequirementId, long ExpectedCaseVersion,
    long ExpectedRequirementVersion, CaseDocumentStatus Status, DateTimeOffset? DueAt, string Note);
public sealed record RetryCaseAttachment(Guid CaseId, Guid AttachmentId, string OriginalName, string ContentType, byte[] Content);
public sealed record SaveInspectionTemplate(Guid? Id, long? ExpectedVersion, string Key, string Title, int SortOrder,
    InspectionAnswerType AnswerType, string[] Options, string Unit, string NormalAnswer, bool AllowAttachments, bool Required, bool Active);
public sealed record InspectionAnswer(Guid ItemId, long ExpectedItemVersion, InspectionItemStatus Status, string Answer, string Note);
public sealed record SaveInspection(Guid CaseId, Guid? InspectionId, long? ExpectedInspectionVersion,
    string OverallConclusion, string PreliminaryDecision, IReadOnlyList<InspectionAnswer> Answers, bool Complete);
public sealed record AssignInspection(Guid CaseId, Guid InspectorEmployeeId, DateTimeOffset? DueAt,
    string Instructions, long? ExpectedInspectionVersion);
public sealed record MarkCaseAcquired(Guid CaseId, long ExpectedCaseVersion, decimal ActualPrice, DateOnly AcquisitionDate, string Comment);
public sealed record CorrectCaseAcquisition(Guid CaseId, long ExpectedCaseVersion, decimal ActualPrice,
    DateOnly AcquisitionDate, string Comment, string Reason);
public sealed record AttachmentContent(string OriginalName, string ContentType, byte[]? Content, string? ExternalUrl);
public sealed record ApplySourceFact(Guid CaseId, Guid CatalogItemId, long ExpectedCaseVersion,
    CaseFactField Field);
public sealed record CorrectPropertyCaseFact(Guid CaseId, long ExpectedCaseVersion, CaseFactField Field,
    string? TextValue, decimal? NumericValue, string Reason);
public sealed record SaveNextAction(Guid CaseId, long ExpectedCaseVersion, long ExpectedTaskVersion,
    WorkTaskType Type, string Title, string Description, DateTimeOffset? DueAt, Guid AssigneeEmployeeId);
public sealed record CreateManualPropertyCase(string Title, string? Location, string? CadastralNumber,
    decimal? Price, decimal? AreaSquareMeters, string Comment, Guid? CommandId = null);
public sealed record ManualPropertyCaseResult(Guid CaseId, string BusinessNumber);
public sealed record NotificationView(Guid Id, Guid CaseId, string Title, DateTimeOffset RecordedAt, bool Read);
public interface IProcurementWorkspace
{
    Task<ProcurementQueuePage> ReadQueueAsync(Subject subject, QueueFilter filter, CancellationToken cancellationToken);
    Task<CaseCard> ReadCardAsync(Subject subject, Guid caseId, CancellationToken cancellationToken);
    Task<Guid?> ResolveLegacyListingAsync(Subject subject, Guid listingId, CancellationToken cancellationToken);
    Task DecideAsync(Subject subject, DecisionCommand command, string correlationId, CancellationToken cancellationToken);
    Task AddNoteAsync(Subject subject, AddCaseNote command, string correlationId, CancellationToken cancellationToken);
    Task AddNegotiationAsync(Subject subject, AddNegotiation command, string correlationId, CancellationToken cancellationToken);
    Task<Guid> AddNegotiationWithIdAsync(Subject subject, AddNegotiation command, string correlationId, CancellationToken cancellationToken);
    Task SaveCheckAsync(Subject subject, SaveCaseCheck command, string correlationId, CancellationToken cancellationToken);
    Task SaveCheckTemplateAsync(Subject subject, SaveCheckTemplate command, string correlationId, CancellationToken cancellationToken);
    Task<Guid> AddAttachmentAsync(Subject subject, AddCaseAttachment command, string correlationId, CancellationToken cancellationToken);
    Task SaveDocumentRequirementAsync(Subject subject, SaveDocumentRequirement command, string correlationId, CancellationToken cancellationToken);
    Task RetryAttachmentAsync(Subject subject, RetryCaseAttachment command, string correlationId, CancellationToken cancellationToken);
    Task SaveInspectionTemplateAsync(Subject subject, SaveInspectionTemplate command, string correlationId, CancellationToken cancellationToken);
    Task<InspectionTaskPage> ReadMyInspectionsAsync(Subject subject, CancellationToken cancellationToken);
    Task<InspectionWorkspaceView> ReadInspectionAsync(Subject subject, Guid caseId, CancellationToken cancellationToken);
    Task AssignInspectionAsync(Subject subject, AssignInspection command, string correlationId, CancellationToken cancellationToken);
    Task<Guid> SaveInspectionAsync(Subject subject, SaveInspection command, string correlationId, CancellationToken cancellationToken);
    Task MarkAcquiredAsync(Subject subject, MarkCaseAcquired command, string correlationId, CancellationToken cancellationToken);
    Task CorrectAcquisitionAsync(Subject subject, CorrectCaseAcquisition command, string correlationId, CancellationToken cancellationToken);
    Task<AttachmentContent> ReadAttachmentAsync(Subject subject, Guid attachmentId, CancellationToken cancellationToken);
    Task ApplySourceFactAsync(Subject subject, ApplySourceFact command, string correlationId, CancellationToken cancellationToken);
    Task CorrectCaseFactAsync(Subject subject, CorrectPropertyCaseFact command, string correlationId, CancellationToken cancellationToken);
    Task SaveNextActionAsync(Subject subject, SaveNextAction command, string correlationId, CancellationToken cancellationToken);
    Task<ManualPropertyCaseResult> CreateManualCaseAsync(Subject subject, CreateManualPropertyCase command, string correlationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<NotificationView>> ReadNotificationsAsync(Subject subject, CancellationToken cancellationToken);
    Task MarkNotificationReadAsync(Subject subject, Guid id, CancellationToken cancellationToken);
}
