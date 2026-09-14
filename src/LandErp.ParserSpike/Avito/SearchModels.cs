using LandErp.ParserSpike.Contracts;

namespace LandErp.ParserSpike.Avito;

/// <summary>Visible business fields captured from one main result card.</summary>
public sealed record CardSnapshot(string ExternalId, string Url, string Title, string Price,
    string Location, string DateText, string PricePerSotka = "", string PreviewDescription = "",
    string SellerName = "", string SellerInfo = "", string Badges = "", string PhotoUrl = "");

/// <summary>Small DOM snapshot; never includes HTML or session state.</summary>
public sealed record SearchSnapshot(bool Captcha, bool Authentication, bool RateLimited,
    bool SourceError, bool Details, bool HasMainResults, CardSnapshot[] Cards);

/// <summary>A listing observation with independent raw/typed field states.</summary>
public sealed record SearchListing(string ExternalId, string Url, string Title,
    ObservedValue<decimal> Price, ObservedValue<decimal> AreaSquareMeters,
    string Location, string DateText, string[] Warnings)
{
    public ObservedValue<decimal> PricePerSotka { get; init; } = new(Presence.NotInspected, null, null, []);
    public string PreviewDescription { get; init; } = "";
    public string SellerName { get; init; } = "";
    public string SellerInfo { get; init; } = "";
    public string Badges { get; init; } = "";
    public string PhotoUrl { get; init; } = "";
}

/// <summary>Search-only export, extending the accepted metadata envelope.</summary>
public sealed record SearchParseResult(ObservationResult Metadata, SearchListing[] Listings);
