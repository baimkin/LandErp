using LandErp.Application.Modules.Catalog.Domain;

namespace LandErp.Application.Modules.Collection.Domain;

public sealed class CollectorAgent
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = "";
    public string CredentialHash { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string VersionText { get; set; } = "";
    public string Capabilities { get; set; } = "";
    public bool CanManageSearches { get; set; }
    public DateTimeOffset? RegisteredAt { get; set; }
    public DateTimeOffset? LastHeartbeatAt { get; set; }
    public long Version { get; set; } = 1;
}
public sealed class SearchConfiguration
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Label { get; set; } = "";
    public CatalogSource Source { get; set; }
    public string Url { get; set; } = "";
    public int MaxPages { get; set; } = 10;
    public Guid? SearchGroupId { get; set; }
    public CollectionScheduleKind ScheduleKind { get; set; } = CollectionScheduleKind.Manual;
    public int? IntervalMinutes { get; set; }
    public string FixedTimesJson { get; set; } = "[]";
    public DateTimeOffset? NextRunAt { get; set; }
    public bool Enabled { get; set; } = true;
    public long Version { get; set; } = 1;
}
public sealed class SearchGroup
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public bool Active { get; set; } = true;
    public DateTimeOffset RecordedAt { get; set; }
    public long Version { get; set; } = 1;
}
public enum CollectionScheduleKind { Manual, Interval, FixedTimes }
public enum CollectionJobState { Pending, Leased, Completed, LimitReached, AwaitingManualAction, Failed, Interrupted }
public sealed class ServerCollectionJob
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    /// <summary>Actual executor. Null until a compatible Collector claims shared work.</summary>
    public Guid? AgentId { get; set; }
    public Guid SearchId { get; set; }
    public CollectionJobState State { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string ResultCode { get; set; } = "";
    public int AcceptedCount { get; set; }
    public int ProcessedCount { get; set; }
    public int NewListingsCount { get; set; }
    public int ChangedListingsCount { get; set; }
    public long Version { get; set; } = 1;
}
public sealed class CollectionDelivery
{
    public Guid Id { get; set; }
    public Guid AgentId { get; set; }
    public Guid JobId { get; set; }
    public string PayloadHash { get; set; } = "";
    public string ReceiptJson { get; set; } = "";
    public DateTimeOffset RecordedAt { get; set; }
}
