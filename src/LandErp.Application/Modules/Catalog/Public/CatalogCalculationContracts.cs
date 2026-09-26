using LandErp.Application.Modules.IdentityAccess.Contracts;

namespace LandErp.Application.Modules.Catalog.Contracts;

public sealed record CatalogCalculationTarget(Guid Id, long ExpectedVersion);
// Null Selected means every result of Filter, across pages. A non-null list means only explicitly selected rows.
public sealed record CatalogCalculationSelection(IncomingCatalogReadFilter Filter,
    IReadOnlyList<CatalogCalculationTarget>? Selected = null);
public sealed record CatalogCalculationPreview(string Stamp, int Total, int WouldChange, int AlreadySet,
    int Ineligible, IReadOnlyDictionary<string, int> Reasons);
public sealed record CatalogCalculationResult(int Total, int Changed, int AlreadySet, int Ineligible,
    IReadOnlyDictionary<string, int> Reasons);

public interface ICatalogCalculationService
{
    Task<CatalogCalculationResult> SetAsync(Subject subject, CatalogCalculationTarget target, bool include,
        string correlationId, CancellationToken cancellationToken);
    Task<CatalogCalculationPreview> PreviewAsync(Subject subject, CatalogCalculationSelection selection, bool include,
        CancellationToken cancellationToken);
    Task<CatalogCalculationResult> ApplyAsync(Subject subject, CatalogCalculationSelection selection, bool include,
        string expectedStamp, string correlationId, CancellationToken cancellationToken);
}
