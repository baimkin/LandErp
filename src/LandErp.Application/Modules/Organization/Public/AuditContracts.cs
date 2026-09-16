using LandErp.Application.Modules.IdentityAccess.Contracts;

namespace LandErp.Application.Modules.Organization.Contracts;

public enum AuditCategory
{
    All,
    DataChanges,
    Security,
    Collection,
    Procurement
}

public sealed record AuditQuery(
    DateOnly? From = null,
    DateOnly? To = null,
    Guid? ActorId = null,
    string? Module = null,
    AuditCategory Category = AuditCategory.All,
    string? Search = null,
    int Page = 1,
    int PageSize = 30);

public sealed record AuditChangeView(string Field, string Before, string After);

public sealed record AuditEventView(
    Guid Id,
    DateTimeOffset RecordedAtUtc,
    DateTimeOffset DisplayTime,
    DateOnly DisplayDate,
    string Module,
    string Category,
    string Tone,
    string Icon,
    string Title,
    string Summary,
    string ActorDisplayName,
    string ActorContext,
    string ObjectDisplayType,
    string ObjectDisplayName,
    bool ObjectArchived,
    IReadOnlyList<AuditChangeView> Changes);

public sealed record AuditActorOption(Guid Id, string Name, bool Archived);
public sealed record AuditModuleOption(string Id, string Name);
public sealed record AuditStatistics(int TotalEvents, int ActiveActors, int DataChanges, int SecurityEvents);

public sealed record AuditPage(
    IReadOnlyList<AuditEventView> Items,
    IReadOnlyList<AuditActorOption> Actors,
    IReadOnlyList<AuditModuleOption> Modules,
    AuditStatistics Statistics,
    int Page,
    int PageSize,
    int TotalCount,
    string BusinessTimeZone,
    DateOnly From,
    DateOnly To)
{
    public int PageCount => Math.Max(1, (int)Math.Ceiling((double)TotalCount / PageSize));
}

public sealed record AuditTechnicalDetails(
    Guid EventId,
    string Action,
    string Entity,
    Guid ObjectId,
    Guid ActorId,
    string CorrelationId,
    string PayloadJson);

public sealed record AuditExport(byte[] Content, string FileName);

public interface IAuditReadService
{
    Task<AuditPage> ReadAsync(Subject subject, AuditQuery query, CancellationToken cancellationToken);
    Task<AuditTechnicalDetails> ReadTechnicalAsync(Subject subject, Guid eventId, CancellationToken cancellationToken);
    Task<AuditExport> ExportCsvAsync(Subject subject, AuditQuery query, CancellationToken cancellationToken);
}
