using LandErp.Application.Modules.IdentityAccess.Contracts;

namespace LandErp.Application.Modules.Catalog.Contracts;

using Domain;

public enum CatalogAgeRange { Any, Today, ThreeDays, Week, OlderThanWeek }
public sealed record IncomingCatalogFilter(string Text = "", CatalogSource? Source = null,
    CatalogDisposition? Disposition = CatalogDisposition.Incoming, CatalogAgeRange Age = CatalogAgeRange.Any,
    decimal? MinPrice = null, decimal? MaxPrice = null, decimal? MinAreaSquareMeters = null,
    decimal? MaxAreaSquareMeters = null, bool AttentionOnly = false, int Offset = 0, int Size = 40);
public sealed record CatalogItemView(Guid Id, CatalogSource Source, string? ExternalId, string? Url, string Title,
    decimal? Price, decimal? PricePerSotka, string Currency, decimal? AreaSquareMeters, string? Location, string? CadastralNumber,
    string? Description, string Provenance, CatalogIngestionKind IngestionKind, CatalogDisposition Disposition,
    string QueueReason, bool AttentionRequired, DateTimeOffset ReceivedAt, DateTimeOffset ChangedAt,
    DateTimeOffset? LastObservedAt, Guid? PropertyCaseId, string? BusinessNumber, string? LinkedCaseStage,
    bool CanResumeCase, long Version);
public sealed record IncomingCatalogSummary(int Incoming, int Attention, int Monitoring, int InWork, int Incomplete);
public sealed record IncomingCatalogPage(IReadOnlyList<CatalogItemView> Items, int Total, IncomingCatalogSummary Summary);
public sealed record CatalogMonitoringView(decimal? TargetTotalPrice, decimal? TargetPricePerSotka,
    DateTimeOffset? StartedAt, decimal? LastEvaluatedPrice, decimal? LastEvaluatedPricePerSotka, DateTimeOffset? LastEvaluatedAt);
public sealed record CatalogEventView(Guid Id, CatalogEventKind Kind, string Message, decimal? ObservedPrice,
    decimal? ObservedPricePerSotka, DateTimeOffset RecordedAt);
public sealed record CatalogItemDetail(CatalogItemView Item, string? SellerName, string? IngressComment,
    CatalogMonitoringView Monitoring, IReadOnlyList<CatalogEventView> Events);
public sealed record CreateManualCatalogItem(CatalogSource Source, string Title, string? Location, decimal? Price,
    decimal? AreaSquareMeters, string? Url, string? ExternalId, string? CadastralNumber, string? Description, string Comment);
public sealed record SetCatalogDisposition(Guid CatalogItemId, long ExpectedVersion, CatalogDisposition Disposition, string Reason);
public sealed record SetCatalogMonitoring(Guid CatalogItemId, long ExpectedVersion, decimal? TargetTotalPrice,
    decimal? TargetPricePerSotka, string Reason);
public sealed record ReviewCatalogDuplicateCandidate(Guid CandidateId, long ExpectedVersion, bool Confirmed);
public sealed record ResumeCatalogItemCase(Guid CatalogItemId, long ExpectedCatalogVersion);
public sealed record TakeCatalogItemToWork(Guid CatalogItemId, Guid? ExistingCaseId = null);
public sealed record CorrectCatalogItemCaseLink(Guid CatalogItemId, long ExpectedCatalogVersion,
    Guid ExpectedCaseId, Guid? TargetCaseId, string Reason);
public sealed record TakeToWorkResult(Guid CaseId, string BusinessNumber, bool Created);
public sealed record CaseLinkTarget(Guid CaseId, string BusinessNumber, string Title);

public interface ICatalogWorkspace
{
    Task<IncomingCatalogPage> ReadIncomingAsync(Subject subject, IncomingCatalogFilter filter, CancellationToken cancellationToken);
    Task<CatalogItemDetail> ReadItemAsync(Subject subject, Guid catalogItemId, CancellationToken cancellationToken);
    Task RegisterViewAsync(Subject subject, Guid catalogItemId, string correlationId, CancellationToken cancellationToken);
    Task<Guid> CreateManualAsync(Subject subject, CreateManualCatalogItem command, string correlationId, CancellationToken cancellationToken);
    Task SetDispositionAsync(Subject subject, SetCatalogDisposition command, string correlationId, CancellationToken cancellationToken);
    Task SetMonitoringAsync(Subject subject, SetCatalogMonitoring command, string correlationId, CancellationToken cancellationToken);
    Task ReviewDuplicateCandidateAsync(Subject subject, ReviewCatalogDuplicateCandidate command, string correlationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<CaseLinkTarget>> ReadLinkTargetsAsync(Subject subject, CancellationToken cancellationToken);
    Task<TakeToWorkResult> TakeToWorkAsync(Subject subject, TakeCatalogItemToWork command, string correlationId, CancellationToken cancellationToken);
    Task CorrectCaseLinkAsync(Subject subject, CorrectCatalogItemCaseLink command, string correlationId, CancellationToken cancellationToken);
    Task<TakeToWorkResult> ResumeCaseAsync(Subject subject, ResumeCatalogItemCase command, string correlationId, CancellationToken cancellationToken);
}
