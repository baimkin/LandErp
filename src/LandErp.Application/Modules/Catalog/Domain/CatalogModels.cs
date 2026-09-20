namespace LandErp.Application.Modules.Catalog.Domain;

public enum CatalogSource { Avito, Cian, Telegram, Referral, Agent, DirectOwner, Manual, Other }
public enum CatalogDisposition { Incoming, Monitoring, InWork, Dismissed, Duplicate, Fake, RemovedAtSource, Sold }
public enum CatalogIngestionKind { Collector, Employee, Migration, Integration }
public enum CatalogEventKind { ReviewStarted, SourceChanged, MonitoringStarted, MonitoringTriggered, Classified, CaseResumed }
public enum DuplicateCandidateStatus { Pending, Confirmed, Rejected, Obsolete }
public enum PhotoFingerprintStatus { Ready, Retry, Unsupported }

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
    public bool AttentionRequired { get; set; } = true;
    public DateTimeOffset? AttentionAt { get; set; }
    public decimal? TargetTotalPrice { get; set; }
    public decimal? TargetPricePerSotka { get; set; }
    public DateTimeOffset? MonitoringStartedAt { get; set; }
    public decimal? LastEvaluatedPrice { get; set; }
    public decimal? LastEvaluatedPricePerSotka { get; set; }
    public DateTimeOffset? LastEvaluatedAt { get; set; }
    public long DataRevision { get; set; } = 1;
    public long Version { get; set; } = 1;
}

public sealed class CatalogEvent
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid CatalogItemId { get; set; }
    public CatalogEventKind Kind { get; set; }
    public string Message { get; set; } = "";
    public decimal? ObservedPrice { get; set; }
    public decimal? ObservedPricePerSotka { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}
public sealed class CatalogDuplicateCandidate
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ListingId { get; set; }
    public Guid CandidateListingId { get; set; }
    public int Score { get; set; }
    public string ReasonsJson { get; set; } = "[]";
    public DuplicateCandidateStatus Status { get; set; } = DuplicateCandidateStatus.Pending;
    public Guid? ReviewedByEmployeeId { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public long Version { get; set; } = 1;
}

public sealed class CatalogPhotoFingerprint
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ListingId { get; set; }
    public int PhotoIndex { get; set; }
    public string UrlHash { get; set; } = "";
    public long? PerceptualHash { get; set; }
    public PhotoFingerprintStatus Status { get; set; } = PhotoFingerprintStatus.Retry;
    public int FailureCount { get; set; }
    public DateTimeOffset? RetryAt { get; set; }
    public long SourceDataRevision { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CatalogDuplicateSettings
{
    public Guid OrganizationId { get; set; }
    public int CandidateThreshold { get; set; } = 55;
    public int DescriptionSimilarityPercent { get; set; } = 55;
    public int AreaTolerancePercent { get; set; } = 15;
    public int PhotoHammingDistance { get; set; } = 8;
    public int StrongPhotoMatches { get; set; } = 2;
    public int CommonPhotoMaxListings { get; set; } = 20;
    public DateTimeOffset UpdatedAt { get; set; }
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
