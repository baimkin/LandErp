using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.Collection.Domain;
using LandErp.Collector.Contracts.V1;

namespace LandErp.Application.Modules.Collection.Contracts;

public sealed record AgentCredential(Guid AgentId, string Token)
{
    public override string ToString() => "Collector credential (token hidden)";
}
public sealed record AgentView(Guid Id, string Name, bool Enabled, bool Online, string Version,
    string Capabilities, DateTimeOffset? LastHeartbeatAt, long Revision, string OperationalStatus, string? CurrentSearch);
public sealed record SearchView(Guid Id, string Label, CatalogSource Source, string Url, int MaxPages,
    Guid? GroupId, string Group, string Schedule, CollectionScheduleKind ScheduleKind, int? IntervalMinutes,
    string[] FixedTimes, bool Enabled, DateTimeOffset? NextRunAt, long Revision, string LastResult);
public sealed record SearchGroupView(Guid Id, string Name, int SortOrder, bool Active, long Revision, int SearchCount);
public sealed record CollectionJobView(Guid Id, string Label, string? Agent, string State, DateTimeOffset CreatedAt,
    DateTimeOffset? LeaseExpiresAt, string ResultCode, int ProcessedCount, int AcceptedCount, int NewListingsCount, int ChangedListingsCount);
public sealed record CollectionAdminView(IReadOnlyList<AgentView> Agents, IReadOnlyList<SearchGroupView> Groups,
    IReadOnlyList<SearchView> Searches, IReadOnlyList<CollectionJobView> Jobs, int ActiveSearches, int PendingJobs, int AttentionJobs);
public sealed record CollectionSchedule(CollectionScheduleKind Kind, int? IntervalMinutes = null, string[]? FixedTimes = null);
public sealed record CreateSearch(string Label, CatalogSource Source, string Url, int MaxPages,
    Guid? SearchGroupId = null, CollectionSchedule? Schedule = null);
public sealed record UpdateSearch(Guid Id, long ExpectedVersion, string Label, CatalogSource Source, string Url,
    int MaxPages, Guid? SearchGroupId, CollectionSchedule Schedule, bool Enabled);
public interface ICollectionAdministration
{
    Task<CollectionAdminView> ReadAsync(Subject subject, CancellationToken cancellationToken);
    Task<AgentCredential> CreateAgentAsync(Subject subject, string name, string correlationId, CancellationToken cancellationToken);
    Task RevokeAgentAsync(Subject subject, Guid agentId, long expectedVersion, string correlationId, CancellationToken cancellationToken);
    Task<AgentCredential> RotateCredentialAsync(Subject subject, Guid agentId, long expectedVersion, string correlationId, CancellationToken cancellationToken);
    Task CreateSearchAsync(Subject subject, CreateSearch command, string correlationId, CancellationToken cancellationToken);
    Task UpdateSearchAsync(Subject subject, UpdateSearch command, string correlationId, CancellationToken cancellationToken);
    Task<Guid> CreateGroupAsync(Subject subject, string name, int sortOrder, string correlationId, CancellationToken cancellationToken);
    Task ArchiveGroupAsync(Subject subject, Guid groupId, long expectedVersion, string correlationId, CancellationToken cancellationToken);
    Task EnqueueAsync(Subject subject, Guid searchId, string correlationId, CancellationToken cancellationToken);
}
public interface ICollectionScheduler
{
    Task<int> RunDueAsync(CancellationToken cancellationToken);
}
public interface ICollectorGateway
{
    Task RegisterAsync(AgentCredential credential, AgentRegistration registration, CancellationToken cancellationToken);
    Task HeartbeatAsync(AgentCredential credential, AgentHeartbeat heartbeat, CancellationToken cancellationToken);
    Task<CollectionWork?> ClaimAsync(AgentCredential credential, CancellationToken cancellationToken);
    Task<CollectionReceipt> AcceptAsync(AgentCredential credential, CollectionResult result, CancellationToken cancellationToken);
}
public sealed class CollectorProtocolException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
