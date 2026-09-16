using LandErp.Application.Foundation.Files;

namespace LandErp.Application.Modules.Procurement.Domain;

public enum NegotiationPriceType { Ask, SellerOffer, BuyerOffer, Agreed }
public enum CaseCheckLevel { Quick, Deep }
public enum CaseCheckStatus { Planned, InProgress, Passed, Issue, Blocked }
public enum CaseAttachmentKind { Photo, Document, Video, Audio, Link }
public enum CaseAttachmentOwner { Case, Negotiation, Check }
public enum CaseFactField { Title, Price, AreaSquareMeters, Location, CadastralNumber }

public sealed class CaseNegotiation
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid PropertyCaseId { get; set; }
    public NegotiationPriceType PriceType { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "RUB";
    public string Channel { get; set; } = "";
    public string Contact { get; set; } = "";
    public string Outcome { get; set; } = "";
    public string Conditions { get; set; } = "";
    public string Comment { get; set; } = "";
    public string NextStep { get; set; } = "";
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
    public CaseAttachmentKind Kind { get; set; }
    public string Label { get; set; } = "";
    public Guid ActorEmployeeId { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
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
