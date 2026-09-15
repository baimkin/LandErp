namespace LandErp.Application.Modules.Catalog.Domain;

public enum CatalogSource { Avito, Cian, Telegram, Referral, Agent, DirectOwner, Manual, Other }
public enum CatalogDisposition { Incoming, Monitoring, InWork, Dismissed, RemovedAtSource, Sold }
public enum CatalogIngestionKind { Collector, Employee, Migration, Integration }

public sealed class Listing
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? TeamId { get; set; }
    public CatalogSource Source { get; set; }
    public string? ExternalId { get; set; }
    public string? Url { get; set; }
    public string? Title { get; set; }
    public decimal? Price { get; set; }
    public string Currency { get; set; } = "RUB";
    public decimal? AreaSquareMeters { get; set; }
    public string? Location { get; set; }
    public string? Description { get; set; }
    public string? SellerName { get; set; }
    public string PhotosJson { get; set; } = "[]";
    public DateTimeOffset? FirstObservedAt { get; set; }
    public DateTimeOffset? LastObservedAt { get; set; }
    public CatalogIngestionKind IngestionKind { get; set; } = CatalogIngestionKind.Collector;
    public Guid? CreatedByEmployeeId { get; set; }
    public string Provenance { get; set; } = "Collector";
    public string? IngressComment { get; set; }
    public string? CadastralNumber { get; set; }
    public CatalogDisposition Disposition { get; set; } = CatalogDisposition.Incoming;
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public string QueueReason { get; set; } = "Новое объявление";
    public long DataRevision { get; set; } = 1;
    public long Version { get; set; } = 1;
}
public sealed class CatalogObservation
{
    public Guid Id { get; set; }
    public Guid ListingId { get; set; }
    public Guid AgentId { get; set; }
    public Guid JobId { get; set; }
    public string ObservationKey { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public string PayloadJson { get; set; } = "";
    public string ChangesJson { get; set; } = "[]";
    public DateTimeOffset ObservedAt { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}
