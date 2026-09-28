namespace LandErp.Application.Modules.Catalog.Contracts;

// Explicit navigation slices; opening one never changes a saved preset or median membership.
public static class IncomingOverviewSelection
{
    public const string New = "new";
    public const string Attention = "attention";
    public static IncomingCatalogReadFilter Filter(string slice) => slice switch
    {
        New => new(new(), WorkingScope: new(Slice: IncomingCatalogPreset.New)),
        Attention => new(new(AttentionOnly: true), WorkingScope: new(IncomingCatalogMode.AllListings)),
        _ => throw new ArgumentException("Неизвестный срез входящих из обзора.")
    };
}
