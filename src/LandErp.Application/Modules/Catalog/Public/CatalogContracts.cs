using LandErp.Application.Modules.IdentityAccess.Contracts;

namespace LandErp.Application.Modules.Catalog.Contracts;

using Domain;

public sealed record IncomingCatalogFilter(string Text = "", CatalogSource? Source = null,
    CatalogDisposition? Disposition = CatalogDisposition.Incoming, int Offset = 0, int Size = 40);
public sealed record CatalogItemView(Guid Id, CatalogSource Source, string? ExternalId, string? Url, string Title,
    decimal? Price, string Currency, decimal? AreaSquareMeters, string? Location, string? CadastralNumber,
    string? Description, string Provenance, CatalogIngestionKind IngestionKind, CatalogDisposition Disposition,
    DateTimeOffset ReceivedAt, Guid? PropertyCaseId, string? BusinessNumber, long Version);
public sealed record IncomingCatalogPage(IReadOnlyList<CatalogItemView> Items, int Total);
public sealed record CreateManualCatalogItem(CatalogSource Source, string Title, string? Location, decimal? Price,
    decimal? AreaSquareMeters, string? Url, string? ExternalId, string? CadastralNumber, string? Description, string Comment);
public sealed record SetCatalogDisposition(Guid CatalogItemId, long ExpectedVersion, CatalogDisposition Disposition, string Reason);
public sealed record TakeCatalogItemToWork(Guid CatalogItemId, Guid? ExistingCaseId = null);
public sealed record TakeToWorkResult(Guid CaseId, string BusinessNumber, bool Created);
public sealed record CaseLinkTarget(Guid CaseId, string BusinessNumber, string Title);

public interface ICatalogWorkspace
{
    Task<IncomingCatalogPage> ReadIncomingAsync(Subject subject, IncomingCatalogFilter filter, CancellationToken cancellationToken);
    Task<Guid> CreateManualAsync(Subject subject, CreateManualCatalogItem command, string correlationId, CancellationToken cancellationToken);
    Task SetDispositionAsync(Subject subject, SetCatalogDisposition command, string correlationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<CaseLinkTarget>> ReadLinkTargetsAsync(Subject subject, CancellationToken cancellationToken);
    Task<TakeToWorkResult> TakeToWorkAsync(Subject subject, TakeCatalogItemToWork command, string correlationId, CancellationToken cancellationToken);
}
