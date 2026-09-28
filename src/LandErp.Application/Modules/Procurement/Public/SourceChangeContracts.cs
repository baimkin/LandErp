using LandErp.Application.Modules.Catalog.Domain;

namespace LandErp.Application.Modules.Procurement.Contracts;

public sealed record SourceChangeValue(string? Text = null, decimal? Number = null, string[]? Photos = null);
public sealed record SourceChangeField(string Name, SourceChangeValue? Before, SourceChangeValue? After,
    string[]? RemovedPhotos = null, string[]? AddedPhotos = null);
public sealed record SourceChangeObservation(Guid Id, DateTimeOffset ObservedAt, IReadOnlyList<SourceChangeField> Fields);
public sealed record SourceChangeComparison(Guid CaseId, Guid LinkId, Guid CatalogItemId, CatalogSource Source,
    string Title, string? Url, long Revision, DateTimeOffset? ObservedAt,
    bool CompleteHistory, IReadOnlyList<SourceChangeObservation> Observations)
{
    public bool CanAcknowledge { get; init; }
}
public sealed record SourceChangeNotice(Guid CatalogItemId, CatalogSource Source, string Title);
public sealed record AcknowledgeSourceChanges(Guid CaseId, Guid LinkId, Guid CatalogItemId, long Revision);
