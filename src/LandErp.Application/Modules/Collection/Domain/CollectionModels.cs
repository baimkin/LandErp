using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Collector.Contracts.V1;

namespace LandErp.Application.Modules.Collection.Domain;

public sealed class CollectorAgent
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = "";
    public string CredentialHash { get; set; } = "";
    public string ActivationHash { get; set; } = "";
    public DateTimeOffset? ActivationExpiresAt { get; set; }
    public DateTimeOffset? ActivationUsedAt { get; set; }
    public bool Enabled { get; set; } = true;
    public string VersionText { get; set; } = "";
    public string Capabilities { get; set; } = "";
    public bool CanManageSearches { get; set; }
    public DateTimeOffset? RegisteredAt { get; set; }
    public DateTimeOffset? LastHeartbeatAt { get; set; }
    public AgentRuntimeState RuntimeState { get; set; } = AgentRuntimeState.Idle;
    public string AttentionCode { get; set; } = "";
    public int ProgressProcessed { get; set; }
    public int? ProgressTotal { get; set; }
    public int? ProgressCurrentPage { get; set; }
    public int? ProgressMaxPages { get; set; }
    public DateTimeOffset? LastActivityAt { get; set; }
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
    public int ConsecutiveFailures { get; set; }
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
public enum CollectionJobState { Pending, Leased, Completed, LimitReached, Partial, RateLimited, AwaitingManualAction, Failed, Interrupted }
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
    public string ReasonCode { get; set; } = "";
    public string WarningsJson { get; set; } = "[]";
    public string? CoverageJson { get; set; }
    public int RetryAttempt { get; set; }
    public Guid? RetryOfJobId { get; set; }
    public DateTimeOffset? RetryAt { get; set; }
    public bool RequiresOperatorAttention { get; set; }
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
public sealed class CollectionSchedulerStatus
{
    public int Id { get; set; } = 1;
    public DateTimeOffset? LastStartedAt { get; set; }
    public DateTimeOffset? LastSucceededAt { get; set; }
    public DateTimeOffset? LastFailedAt { get; set; }
    public int LastQueuedCount { get; set; }
    public string LastFailureCode { get; set; } = "";
}
