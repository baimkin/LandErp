using System.Globalization;
using System.Text.RegularExpressions;
using LandErp.ParserSpike.Contracts;

namespace LandErp.ParserSpike.Avito;

/// <summary>Conservative classification and typing for visible main search cards.</summary>
public static partial class SearchParser
{
    /// <summary>Classifies before extracting; protection states take precedence over cards.</summary>
    public static SearchParseResult Parse(SearchSnapshot snapshot, Uri? requestedUrl,
        Uri? finalUrl, DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        PageClassification classification = snapshot switch
        {
            { Captcha: true } => PageClassification.Captcha,
            { RateLimited: true } => PageClassification.RateLimited,
            { Authentication: true } => PageClassification.AuthenticationRequired,
            { SourceError: true } => PageClassification.SourceError,
            { Details: true } => PageClassification.ListingDetails,
            { HasMainResults: true, Cards.Length: > 0 } => PageClassification.SearchResults,
            _ => PageClassification.Unknown
        };
        if (classification != PageClassification.SearchResults)
        {
            Outcome outcome = classification is PageClassification.Captcha or
                PageClassification.AuthenticationRequired or PageClassification.RateLimited
                ? Outcome.Attention : Outcome.Failure;
            return new(Metadata(classification, outcome, ["PAGE_NOT_SEARCH_RESULTS"], []), []);
        }

        List<SearchListing> listings = [];
        HashSet<string> ids = new(StringComparer.Ordinal);
        List<string> errors = [];
        foreach (CardSnapshot card in snapshot.Cards)
        {
            if (!IdPattern().IsMatch(card.ExternalId) || !PublicListingUrl(card.Url, out Uri? directUrl)
                || !directUrl!.AbsolutePath.EndsWith("_" + card.ExternalId, StringComparison.Ordinal))
            {
                errors.Add("ITEM_ID_OR_URL_INVALID");
                continue;
            }
            if (!ids.Add(card.ExternalId))
            {
                errors.Add("ITEM_DUPLICATE_ID");
                continue;
            }
            List<string> warnings = [];
            if (string.IsNullOrWhiteSpace(card.Title)) warnings.Add("TITLE_ABSENT");
            if (string.IsNullOrWhiteSpace(card.Location)) warnings.Add("LOCATION_ABSENT");
            if (string.IsNullOrWhiteSpace(card.DateText)) warnings.Add("DATE_ABSENT");
            ObservedValue<decimal> price = Price(card.Price);
            ObservedValue<decimal> area = Area(card.Title);
            warnings.AddRange(price.Warnings);
            warnings.AddRange(area.Warnings);
            listings.Add(new(card.ExternalId, directUrl!.AbsoluteUri, card.Title,
                price, area, card.Location, card.DateText, warnings.ToArray())
            {
                PricePerSotka = UnitPrice(card.PricePerSotka),
                PreviewDescription = card.PreviewDescription,
                SellerName = card.SellerName,
                SellerInfo = card.SellerInfo,
                Badges = card.Badges,
                PhotoUrl = PublicPhotoUrl(card.PhotoUrl)
            });
        }

        if (listings.Count == 0) errors.Add("NO_VALID_MAIN_ITEMS");
        Outcome resultOutcome = listings.Count == 0 ? Outcome.Failure :
            errors.Count > 0 ? Outcome.Attention : Outcome.Success;
        return new(Metadata(classification, resultOutcome, errors.Distinct().ToArray(),
            ["EXPERIMENTAL_SELECTORS"]), listings.ToArray());

        ObservationResult Metadata(PageClassification page, Outcome outcome, string[] resultErrors,
            string[] resultWarnings) => new("1.0", "AVITO", "0.2.1", SafeUrl(requestedUrl),
                SafeUrl(finalUrl), observedAt, page, outcome, resultWarnings, resultErrors, Guid.NewGuid(), []);
    }

    /// <summary>Accepts public Avito navigation URLs without credentials or fragment.</summary>
    public static bool IsAvitoUrl(Uri? url) => url is { IsAbsoluteUri: true } &&
        url.Scheme == Uri.UriSchemeHttps && url.Port == 443 && url.UserInfo.Length == 0 &&
        url.Fragment.Length == 0 && url.Host is "avito.ru" or "www.avito.ru";

    private static Uri? SafeUrl(Uri? url) => IsAvitoUrl(url)
        ? new Uri(url!.GetLeftPart(UriPartial.Path)) : null;

    private static bool PublicListingUrl(string text, out Uri? url)
    {
        bool valid = Uri.TryCreate(text, UriKind.Absolute, out url) && IsAvitoUrl(url);
        url = valid ? SafeUrl(url) : null;
        return valid;
    }

    private static ObservedValue<decimal> Price(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new(Presence.Absent, null, null, ["PRICE_ABSENT"]);
        string normalized = raw.Replace("\u00a0", "", StringComparison.Ordinal)
            .Replace("\u202f", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal)
            .Replace("₽", "", StringComparison.Ordinal).Replace("руб.", "", StringComparison.Ordinal);
        if (decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out decimal price) && price >= 0)
            return new(Presence.Present, raw, price, []);
        return new(Presence.ParseFailed, raw, null, ["PRICE_PARSE_FAILED"]);
    }

    private static string PublicPhotoUrl(string text) => Uri.TryCreate(text, UriKind.Absolute, out Uri? url)
        && url.Scheme == Uri.UriSchemeHttps && url.UserInfo.Length == 0
        && (url.Host == "avito.st" || url.Host.EndsWith(".avito.st", StringComparison.Ordinal))
        ? url.GetLeftPart(UriPartial.Path) : "";

    private static ObservedValue<decimal> UnitPrice(string raw)
    {
        ObservedValue<decimal> value = Price(PricePerSotkaPattern().Replace(raw, ""));
        return new(value.Presence, value.Raw is null ? null : raw, value.Parsed,
            value.Warnings.Select(code => code.Replace("PRICE_", "UNIT_PRICE_", StringComparison.Ordinal)).ToArray());
    }

    [GeneratedRegex(@"\s*(?:за\s+сот(?:ку|ку\.)?|/\s*сот(?:\.)?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PricePerSotkaPattern();

    private static ObservedValue<decimal> Area(string title)
    {
        Match match = AreaPattern().Match(title);
        if (!match.Success) return new(Presence.Absent, null, null, ["AREA_ABSENT"]);
        string raw = match.Value;
        if (!decimal.TryParse(match.Groups[1].Value.Replace(',', '.'), NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out decimal area))
            return new(Presence.ParseFailed, raw, null, ["AREA_PARSE_FAILED"]);
        string unit = match.Groups[2].Value.ToLowerInvariant();
        decimal multiplier = unit.StartsWith("сот", StringComparison.Ordinal) ? 100m : unit == "га" ? 10000m : 1m;
        try { return new(Presence.Present, raw, checked(area * multiplier), []); }
        catch (OverflowException) { return new(Presence.ParseFailed, raw, null, ["AREA_PARSE_FAILED"]); }
    }

    [GeneratedRegex(@"^\d{5,20}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"(\d+(?:[.,]\d+)?)\s*(сот(?:ок|ки|ка|\.)?|га|м²|м2|м\^2)(?![а-яa-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AreaPattern();
}
