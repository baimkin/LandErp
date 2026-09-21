using LandErp.Application.Foundation.Files;

namespace LandErp.Application.Modules.Procurement.Domain;

public enum CaseCheckLevel { Quick, Deep }
public enum CaseCheckStatus { Planned, InProgress, Passed, Issue, Blocked }
public enum CaseAttachmentKind { Photo, Document, Video, Audio, Link }
public enum CaseAttachmentOwner { Case, Negotiation, Check, Inspection, InspectionItem }
public enum CaseDocumentStatus { Missing, Requested, Received, Verified }
public enum CaseFactField { Title, Price, AreaSquareMeters, Location, CadastralNumber }
public enum InspectionAnswerType { Boolean, Number, Percentage, Choice, Text }
public enum InspectionStatus { Draft, Completed }
public enum InspectionItemStatus { Unanswered, Answered, NotChecked }

public sealed class CaseNegotiation
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid PropertyCaseId { get; set; }
    public decimal? SellerPrice { get; set; }
    public decimal? BuyerOffer { get; set; }
    public decimal? AgreedPrice { get; set; }
    public string Currency { get; set; } = "RUB";
    public string Channel { get; set; } = "";
    public string Contact { get; set; } = "";
    public string Outcome { get; set; } = "";
    public string Conditions { get; set; } = "";
    public string Comment { get; set; } = "";
    public string NextStep { get; set; } = "";
    public DateTimeOffset? NextStepDueAt { get; set; }
    public Guid AuthorEmployeeId { get; set; }
    public DateTimeOffset EffectiveAt { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}

public sealed class CaseCheck
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid PropertyCaseId { get; set; }
    public CaseCheckLevel Level { get; set; }
    public string Title { get; set; } = "";
    public string DescriptionSnapshot { get; set; } = "";
    public Guid? TemplateItemId { get; set; }
    public long? TemplateItemVersion { get; set; }
    public CaseCheckStatus Status { get; set; }
    public Guid? ResponsibleEmployeeId { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public decimal? Cost { get; set; }
    public string Currency { get; set; } = "RUB";
    public string Result { get; set; } = "";
    public bool Blocker { get; set; }
    public Guid AuthorEmployeeId { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public long Version { get; set; } = 1;
}

public sealed class CaseAttachment
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid PropertyCaseId { get; set; }
    public Guid StoredFileId { get; set; }
    public CaseAttachmentOwner OwnerType { get; set; }
    public Guid? NegotiationId { get; set; }
    public Guid? CheckId { get; set; }
    public Guid? InspectionId { get; set; }
    public Guid? InspectionItemId { get; set; }
    public Guid? DocumentRequirementId { get; set; }
    public CaseAttachmentKind Kind { get; set; }
    public string Label { get; set; } = "";
    public string Description { get; set; } = "";
    public Guid ActorEmployeeId { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}

public sealed class CaseDocumentRequirement
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid PropertyCaseId { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string ExpectedSource { get; set; } = "";
    public CaseDocumentStatus Status { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public string Note { get; set; } = "";
    public Guid UpdatedByEmployeeId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; } = 1;
}

public sealed class CaseCheckTemplateItem
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Title { get; set; } = "";
    public CaseCheckLevel Level { get; set; }
    public string Description { get; set; } = "";
    public int SortOrder { get; set; }
    public bool Active { get; set; } = true;
    public long Version { get; set; } = 1;
}

public sealed class InspectionTemplateItem
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public int SortOrder { get; set; }
    public InspectionAnswerType AnswerType { get; set; }
    public string OptionsJson { get; set; } = "[]";
    public string Unit { get; set; } = "";
    public string NormalAnswer { get; set; } = "";
    public bool AllowAttachments { get; set; } = true;
    public bool Required { get; set; }
    public bool Active { get; set; } = true;
    public long Version { get; set; } = 1;
}

public sealed class SiteInspection
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid PropertyCaseId { get; set; }
    public InspectionStatus Status { get; set; } = InspectionStatus.Draft;
    public string OverallConclusion { get; set; } = "";
    public string PreliminaryDecision { get; set; } = "";
    public Guid InspectorEmployeeId { get; set; }
    public Guid? RequestedByEmployeeId { get; set; }
    public DateTimeOffset? RequestedAt { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public string Instructions { get; set; } = "";
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public long Version { get; set; } = 1;
}

public sealed class SiteInspectionItem
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid InspectionId { get; set; }
    public Guid TemplateItemId { get; set; }
    public long TemplateItemVersion { get; set; }
    public string TitleSnapshot { get; set; } = "";
    public int SortOrderSnapshot { get; set; }
    public InspectionAnswerType AnswerTypeSnapshot { get; set; }
    public string OptionsJsonSnapshot { get; set; } = "[]";
    public string UnitSnapshot { get; set; } = "";
    public string NormalAnswerSnapshot { get; set; } = "";
    public bool AllowAttachmentsSnapshot { get; set; }
    public bool RequiredSnapshot { get; set; }
    public InspectionItemStatus Status { get; set; }
    public string Answer { get; set; } = "";
    public string Note { get; set; } = "";
    public long Version { get; set; } = 1;
}

/// <summary>Append-only evidence that a user explicitly accepted a source value as a case-owned working fact.</summary>
public sealed class PropertyCaseFactRevision
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid PropertyCaseId { get; set; }
    public Guid CatalogItemId { get; set; }
    public CaseFactField Field { get; set; }
    public string Value { get; set; } = "";
    public Guid VerifiedByEmployeeId { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}
