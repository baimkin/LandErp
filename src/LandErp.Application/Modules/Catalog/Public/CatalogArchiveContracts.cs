using LandErp.Application.Modules.IdentityAccess.Contracts;

namespace LandErp.Application.Modules.Catalog.Contracts;

public sealed record CatalogBulkTarget(Guid Id, long ExpectedVersion);
// Null Selected means all matching results, across pages, except ExcludedIds.
public sealed record CatalogBulkSelection(IncomingCatalogReadFilter Filter,
    IReadOnlyList<CatalogBulkTarget>? Selected = null, IReadOnlyList<Guid>? ExcludedIds = null);
public sealed record CatalogArchivePreview(string Stamp, int Total, int WouldArchive,
    int AlreadyArchived, int WouldExcludeFromMedian);
public sealed record CatalogArchiveResult(int Total, int Archived, int AlreadyArchived, int ExcludedFromMedian);

public interface ICatalogArchiveService
{
    Task<CatalogArchivePreview> PreviewAsync(Subject subject, CatalogBulkSelection selection,
        bool excludeFromMedian, CancellationToken cancellationToken);
    Task<CatalogArchiveResult> ApplyAsync(Subject subject, CatalogBulkSelection selection,
        bool excludeFromMedian, string reason, string expectedStamp, string correlationId,
        CancellationToken cancellationToken);
}
