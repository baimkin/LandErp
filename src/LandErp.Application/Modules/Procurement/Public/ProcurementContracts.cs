using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Collector.Contracts.V1;

namespace LandErp.Application.Modules.Procurement.Contracts;

public enum ProcurementAction { Monitor, Clarify, Reject, Forward, Return, Approve }
public sealed record QueueFilter(string Text = "", string Stage = "", CatalogSource? Source = null, bool ChangedOnly = false, int Offset = 0, int Size = 40);
public sealed record QueueItem(Guid CaseId, string BusinessNumber, string Title, CatalogSource[] Sources,
    decimal? Price, string Currency, decimal? Area, string? Location, string Stage, string? Assignee, DateTimeOffset? DueAt,
    string Reason, string[] UnknownFields, bool Changed, long SourceRevision, long CaseVersion);
public sealed record ProcurementQueuePage(IReadOnlyList<QueueItem> Items, int Total);
public sealed record TimelineItem(Guid Id, string Kind, string Title, string Body, string Actor, string? Target,
    DateTimeOffset RecordedAt, DateTimeOffset? EffectiveAt, DateTimeOffset? DueAt);
public sealed record ObservationView(Guid Id, Guid CatalogItemId, DateTimeOffset ObservedAt, DateTimeOffset RecordedAt, ListingData Data, string[] Changes);
public sealed record CaseSourceView(Guid CatalogItemId, CatalogSource Source, string? ExternalId, string? Url, string Title,
    decimal? Price, decimal? AreaSquareMeters, string? Location, string Provenance, DateTimeOffset? LastObservedAt);
public sealed record DecisionTarget(Guid EmployeeId, string Name);
public sealed record CaseCard(QueueItem Item, string? Description, string? SellerName, string[] Photos,
    IReadOnlyList<CaseSourceView> Sources, IReadOnlyList<TimelineItem> Timeline, IReadOnlyList<ObservationView> Observations,
    IReadOnlyList<DecisionTarget> Heads, IReadOnlyList<DecisionTarget> Managers, Guid ManagerEmployeeId, bool CanManagerDecide, bool CanHeadDecide);
public sealed record DecisionCommand(Guid CaseId, long ExpectedCaseVersion, long ExpectedSourceRevision,
    ProcurementAction Action, string Reason, string Clarification, Guid? TargetEmployeeId, DateTimeOffset? DueAt);
public sealed record AddCaseNote(Guid CaseId, long ExpectedCaseVersion, string Text, bool Contact, string ContactResult, DateTimeOffset? EffectiveAt);
public sealed record NotificationView(Guid Id, Guid CaseId, string Title, DateTimeOffset RecordedAt, bool Read);
public interface IProcurementWorkspace
{
    Task<ProcurementQueuePage> ReadQueueAsync(Subject subject, QueueFilter filter, CancellationToken cancellationToken);
    Task<CaseCard> ReadCardAsync(Subject subject, Guid caseId, CancellationToken cancellationToken);
    Task<Guid?> ResolveLegacyListingAsync(Subject subject, Guid listingId, CancellationToken cancellationToken);
    Task DecideAsync(Subject subject, DecisionCommand command, string correlationId, CancellationToken cancellationToken);
    Task AddNoteAsync(Subject subject, AddCaseNote command, string correlationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<NotificationView>> ReadNotificationsAsync(Subject subject, CancellationToken cancellationToken);
    Task MarkNotificationReadAsync(Subject subject, Guid id, CancellationToken cancellationToken);
}
