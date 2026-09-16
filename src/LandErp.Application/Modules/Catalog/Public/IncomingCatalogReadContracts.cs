using LandErp.Application.Modules.IdentityAccess.Contracts;

namespace LandErp.Application.Modules.Catalog.Contracts;

using Domain;

/// <summary>Server-backed working views that can be derived from existing Catalog facts without new persistence.</summary>
public enum IncomingCatalogPreset { PriceChanged, Incomplete, ReturnedFromMonitoring }
public enum IncomingCatalogRowState { Normal, PriceChanged, ReturnedFromMonitoring, Incomplete }
public enum IncomingCatalogSortField { ChangedAt, Price, Area }
public enum IncomingCatalogSortDirection { Descending, Ascending }

public sealed record IncomingCatalogReadFilter(IncomingCatalogFilter Base, Guid? SearchConfigurationId = null,
    IncomingCatalogPreset? Preset = null, IncomingCatalogSortField SortField = IncomingCatalogSortField.ChangedAt,
    IncomingCatalogSortDirection SortDirection = IncomingCatalogSortDirection.Descending);
public sealed record IncomingSearchConfigurationView(Guid Id, string Label, CatalogSource Source);
public sealed record IncomingCatalogRowRead(Guid CatalogItemId, Guid? SearchConfigurationId,
    string? SearchConfigurationLabel, int CompletenessPercent, IncomingCatalogRowState RowState,
    bool PriceChanged, bool ReturnedFromMonitoring, string? ThumbnailUrl = null, int PhotoCount = 0);
public sealed record IncomingCatalogReadSummary(int Incoming, int Attention, int Monitoring, int InWork, int Incomplete,
    int PriceChanged, int ReturnedFromMonitoring);
public sealed record IncomingCatalogReadPage(IReadOnlyList<CatalogItemView> Items, int Total,
    IncomingCatalogReadSummary Summary, IReadOnlyList<IncomingSearchConfigurationView> SearchConfigurations,
    IReadOnlyDictionary<Guid, IncomingCatalogRowRead> Rows);
public sealed record IncomingCatalogDetailRead(CatalogItemDetail Detail, IReadOnlyList<string> PhotoUrls,
    Guid? SearchConfigurationId, string? SearchConfigurationLabel, int CompletenessPercent,
    IncomingCatalogRowState RowState, bool PriceChanged, bool ReturnedFromMonitoring);

public interface IIncomingCatalogReadService
{
    Task<IncomingCatalogReadPage> ReadAsync(Subject subject, IncomingCatalogReadFilter filter, CancellationToken cancellationToken);
    Task<IncomingCatalogDetailRead> ReadDetailAsync(Subject subject, Guid catalogItemId, CancellationToken cancellationToken);
}
