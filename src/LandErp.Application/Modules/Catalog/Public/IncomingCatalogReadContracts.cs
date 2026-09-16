using LandErp.Application.Modules.IdentityAccess.Contracts;

namespace LandErp.Application.Modules.Catalog.Contracts;

using Domain;

/// <summary>Server-backed working views for Incoming. Source-derived land types are convenience classifications, not legal VRI/category facts.</summary>
public enum IncomingCatalogPreset { PriceChanged, Incomplete, ReturnedFromMonitoring }
public enum IncomingCatalogRowState { Normal, PriceChanged, ReturnedFromMonitoring, Incomplete }
public enum IncomingCatalogSortField { ChangedAt, Price, Area }
public enum IncomingCatalogSortDirection { Descending, Ascending }
public enum IncomingLandType { Izhs, Snt, Dnp, Lph, Gardening, Kfh, Industrial, Other }
public enum IncomingCatalogMatchField { SellerName }

public sealed record IncomingCatalogReadFilter(IncomingCatalogFilter Base, Guid? SearchConfigurationId = null,
    IncomingCatalogPreset? Preset = null, IncomingCatalogSortField SortField = IncomingCatalogSortField.ChangedAt,
    IncomingCatalogSortDirection SortDirection = IncomingCatalogSortDirection.Descending,
    Guid? SearchGroupId = null, decimal? MinPricePerSotka = null, decimal? MaxPricePerSotka = null,
    IReadOnlyList<IncomingLandType>? LandTypes = null);
public sealed record IncomingSearchGroupView(Guid Id, string Name, int SortOrder);
public sealed record IncomingSearchConfigurationView(Guid Id, string Label, CatalogSource Source, Guid? SearchGroupId);
public sealed record IncomingCatalogRowRead(Guid CatalogItemId, Guid? SearchConfigurationId,
    string? SearchConfigurationLabel, int CompletenessPercent, IncomingCatalogRowState RowState,
    bool PriceChanged, bool ReturnedFromMonitoring, string? ThumbnailUrl, int PhotoCount,
    IncomingLandType[] LandTypes, IncomingCatalogMatchField? SearchMatchedField = null,
    string? SearchMatchedValue = null);
public sealed record IncomingCatalogReadSummary(int Incoming, int Attention, int Monitoring, int InWork, int Incomplete,
    int PriceChanged, int ReturnedFromMonitoring);
public sealed record IncomingCatalogReadPage(IReadOnlyList<CatalogItemView> Items, int Total,
    IncomingCatalogReadSummary Summary, IReadOnlyList<IncomingSearchGroupView> SearchGroups,
    IReadOnlyList<IncomingSearchConfigurationView> SearchConfigurations,
    IReadOnlyDictionary<Guid, IncomingCatalogRowRead> Rows);
public sealed record IncomingCatalogDetailRead(CatalogItemDetail Detail, IReadOnlyList<string> PhotoUrls,
    Guid? SearchConfigurationId, string? SearchConfigurationLabel, int CompletenessPercent,
    IncomingCatalogRowState RowState, bool PriceChanged, bool ReturnedFromMonitoring,
    IncomingLandType[] LandTypes);

public sealed record IncomingFilterPresetCriteriaV1(int SchemaVersion, CatalogSource? Source,
    Guid? SearchConfigurationId, CatalogDisposition? Disposition, CatalogAgeRange Age,
    decimal? MinTotalPrice, decimal? MaxTotalPrice, decimal? MinPricePerSotka, decimal? MaxPricePerSotka,
    decimal? MinAreaSquareMeters, decimal? MaxAreaSquareMeters, IncomingLandType[] LandTypes,
    bool AttentionOnly, IncomingCatalogSortField SortField, IncomingCatalogSortDirection SortDirection,
    IncomingCatalogPreset? Preset = null);
public sealed record IncomingFilterPresetView(Guid Id, Guid? SearchGroupId, string Name,
    IncomingFilterPresetCriteriaV1 Criteria, int SortOrder, long Version);
public sealed record CreateIncomingFilterPreset(Guid? SearchGroupId, string Name, IncomingFilterPresetCriteriaV1 Criteria);
public sealed record RenameIncomingFilterPreset(Guid Id, long ExpectedVersion, string Name);
public sealed record DeleteIncomingFilterPreset(Guid Id, long ExpectedVersion);

public interface IIncomingCatalogReadService
{
    Task<IncomingCatalogReadPage> ReadAsync(Subject subject, IncomingCatalogReadFilter filter, CancellationToken cancellationToken);
    Task<IncomingCatalogDetailRead> ReadDetailAsync(Subject subject, Guid catalogItemId, CancellationToken cancellationToken);
}

public interface IIncomingFilterPresetService
{
    Task<IReadOnlyList<IncomingFilterPresetView>> ReadAsync(Subject subject, CancellationToken cancellationToken);
    Task<IncomingFilterPresetView> CreateAsync(Subject subject, CreateIncomingFilterPreset command, CancellationToken cancellationToken);
    Task<IncomingFilterPresetView> RenameAsync(Subject subject, RenameIncomingFilterPreset command, CancellationToken cancellationToken);
    Task DeleteAsync(Subject subject, DeleteIncomingFilterPreset command, CancellationToken cancellationToken);
}
