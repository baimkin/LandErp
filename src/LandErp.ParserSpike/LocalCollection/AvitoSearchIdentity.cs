namespace LandErp.ParserSpike.LocalCollection;

/// <summary>List URLs retain the declared zone reference; map mode additionally requires geometry.</summary>
public static class AvitoSearchIdentity
{
    // Observed serialization of the neutral land-search form. This is deliberately
    // an exact token, not a decoder or permission to ignore arbitrary opaque filters.
    public const string NeutralLandFilter = "ASgBAgICAUSq3xUC";

    public static bool ConfirmsList(string requested, string current, string sourceMapLink, bool neutralForm)
    {
        try
        {
            return SameDraw(requested, current) && SameDraw(requested, sourceMapLink)
                && SameFilters(requested, current, neutralForm) && SameFilters(requested, sourceMapLink, neutralForm);
        }
        catch (ArgumentException) { return false; }
    }
    public static bool ConfirmsListLinks(string requested, string current, string[] sourceMapLinks, bool neutralForm)
        => sourceMapLinks.Length > 0 && sourceMapLinks.All(link => ConfirmsList(requested, current, link, neutralForm));

    public static bool Confirmed(string requested, string current, MapScope? scope, bool neutralForm)
    {
        try
        {
            string? draw = AvitoMapData.DrawId(requested);
            if (scope is null || !scope.ZoneConfirmed || scope.RequiresDraw != (draw is not null) || !SameDraw(requested, current)) return false;
            if (draw is not null && (!scope.RequiresDraw || scope.DrawId != draw
                || scope.Rings.Length == 0 || scope.Rings.Any(ring => ring.Length < 4))) return false;
            return SameFilters(requested, current, neutralForm);
        }
        catch (ArgumentException) { return false; }
    }

    private static bool SameDraw(string requested, string other)
    {
        // Normalize rejects foreign hosts/credentials; unique lookup rejects ambiguous draw references.
        _ = SearchUrls.Normalize(other, SourceSite.Avito);
        string? expected = UniqueValue(requested, "drawId"), actual = UniqueValue(other, "drawId");
        return expected == actual && (expected is null || !string.IsNullOrWhiteSpace(expected));
    }

    private static string? UniqueValue(string url, string key)
    {
        string[] values = Terms(url).Where(pair => pair.Key == key).Select(pair => pair.Value).ToArray();
        if (values.Length > 1) throw new ArgumentException("AVITO_DUPLICATE_FILTER");
        return values.SingleOrDefault();
    }

    private static KeyValuePair<string, string>[] Terms(string url)
        => new Uri(SearchUrls.Normalize(url, SourceSite.Avito).Url).Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries).Select(term =>
            {
                string[] pair = term.Split('=', 2);
                return new KeyValuePair<string, string>(Uri.UnescapeDataString(pair[0]), Uri.UnescapeDataString(pair[1]));
            }).ToArray();

    public static bool SameFilters(string requested, string other, bool neutralForm)
    {
        // Even with a trusted polygon, an ambiguous draw parameter or foreign URL is invalid.
        _ = UniqueValue(requested, "drawId"); _ = UniqueValue(other, "drawId");
        string? originalFilter = UniqueValue(requested, "f"), actualFilter = UniqueValue(other, "f");
        bool neutralAddition = originalFilter is null && actualFilter == NeutralLandFilter && neutralForm
            && AvitoMapData.DrawId(requested) is not null
            && new Uri(requested).AbsolutePath.EndsWith("/zemelnye_uchastki", StringComparison.Ordinal);
        string WithoutGeometry(string url, bool removeDefault)
        {
            string query = string.Join('&', Terms(url).Where(pair => pair.Key is not ("map" or "drawId")
                && !(removeDefault && pair.Key == "f"))
                .Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
            return new Uri(url).GetLeftPart(UriPartial.Path) + (query.Length == 0 ? "" : "?" + query);
        }
        return SearchUrls.SameSearch(WithoutGeometry(requested, false), WithoutGeometry(other, neutralAddition), SourceSite.Avito);
    }
}
