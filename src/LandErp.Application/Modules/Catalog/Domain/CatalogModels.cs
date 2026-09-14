using LandErp.Collector.Contracts.V1;

namespace LandErp.Application.Modules.Catalog.Domain;

public sealed class Listing
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? TeamId { get; set; }
    public ListingSource Source { get; set; }
    public string ExternalId { get; set; } = "";
    public string Url { get; set; } = "";
    public string? Title { get; set; }
    public decimal? Price { get; set; }
    public string Currency { get; set; } = "RUB";
    public decimal? AreaSquareMeters { get; set; }
    public string? Location { get; set; }
    public string? Description { get; set; }
    public string? SellerName { get; set; }
    public string PhotosJson { get; set; } = "[]";
    public DateTimeOffset FirstObservedAt { get; set; }
    public DateTimeOffset LastObservedAt { get; set; }
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
