namespace LandErp.Application.Modules.Collection.Domain;

public sealed class SearchGroupMarketSettings
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid SearchGroupId { get; set; }
    public int PeriodDays { get; set; } = 30;
    public string[] AllowedPropertyTypes { get; set; } = [];
    public decimal? MinPricePerSotka { get; set; }
    public decimal? MaxPricePerSotka { get; set; }
    public long Version { get; set; } = 1;
}
