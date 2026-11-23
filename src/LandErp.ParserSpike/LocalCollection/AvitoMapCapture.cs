using System.Globalization;
using System.Text.Json;
using Microsoft.Playwright;

namespace LandErp.ParserSpike.LocalCollection;

/// <summary>Business data from two public map responses. Raw responses live only during decoding;
/// a page lease owns its capture, so responses cannot leak into another queued search.</summary>
public sealed class AvitoMapData
{
    private readonly Dictionary<string, ListingObservation> items = new(StringComparer.Ordinal);
    private readonly List<ListingObservation> changes = [];
    private readonly HashSet<string> errors = new(StringComparer.Ordinal);
    private GeoPoint[][] rings = [];
    private GeoPoint[][][] observedAreas = [];
    private string? geometryRequestDraw;
    private string? polygon;
    private string? markersPolygon;
    private string? pinsPolygon;
    private string? zoneError;
    private int? expected;
    private readonly HashSet<string> receivedIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> excludedIds = new(StringComparer.Ordinal);
    private readonly List<MapBatchDiagnostic> batchDiagnostics = [];
    private readonly List<MapListingPoint[]> diagnosticPoints = [];
    public int Batches { get; private set; }
    public PageKind Kind { get; private set; } = PageKind.SearchResults;
    public void Ingest(string endpoint, string json, DateTimeOffset time, string? requestDrawId = null)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            MapResponseSummary? summary = JsonResponseDiagnostics.SummarizeMap(root, endpoint);
            if (summary is null) return;
            Kind = PageKind.SearchResults;
            if (!endpoint.EndsWith("/items", StringComparison.Ordinal))
            {
                observedAreas = SearchAreaGeometry.Read(summary.DrawGeometry);
                geometryRequestDraw = requestDrawId;
                string? signature = SearchAreaGeometry.Signature(observedAreas);
                bool isPins = endpoint.EndsWith("/pins", StringComparison.Ordinal);
                // Retain the last valid signature per format so a missing response
                // cannot erase evidence of a disagreement between the sources.
                if (signature is not null)
                {
                    if (isPins) pinsPolygon = signature;
                    else markersPolygon = signature;
                }
                if (pinsPolygon is not null && markersPolygon is not null && pinsPolygon != markersPolygon)
                {
                    observedAreas = [];
                    zoneError = "Ответы pins и markers содержат разные контуры";
                    errors.Add("MAP_DRAW_SOURCE_CONFLICT");
                    return;
                }
                if (signature is null) zoneError = "Геометрия выделенной области не получена или не распознана";
                else if (polygon is not null && polygon != signature) zoneError = "Выделенная зона изменилась: восстановите исходную область";
                else { polygon = signature; rings = observedAreas[0]; zoneError = null; }
                return;
            }
            if (Get(root, "items").ValueKind != JsonValueKind.Array) { errors.Add("MAP_ITEMS_SCHEMA_UNKNOWN"); return; }
            Batches++;
            if (summary.TotalCount is not null)
            {
                if (expected is not null && expected != summary.TotalCount) errors.Add("MAP_COUNT_CHANGED");
                expected = summary.TotalCount;
            }
            int received = Get(root, "items").GetArrayLength(), newIds = 0, repeats = 0, recommendations = 0, invalid = 0, invalidCards = 0;
            List<string> batchIds = [];
            List<MapListingPoint> parsedPoints = [];
            Dictionary<string, MapListingPoint> points = summary.Listings.GroupBy(x => x.Id, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
            foreach (JsonElement item in Get(root, "items").EnumerateArray().Take(5000))
            {
                string id = Text(Get(item, "id")) ?? "";
                if (System.Text.RegularExpressions.Regex.IsMatch(id, @"^\d{5,20}$"))
                { batchIds.Add(id); if (receivedIds.Add(id)) newIds++; else repeats++; }
                if (True(Get(item, "isSimilarToSearch")) || True(Get(item, "isOutGeo")))
                {
                    recommendations++; excludedIds.Add(id); items.Remove(id);
                    changes.RemoveAll(change => change.ExternalId == id); continue;
                }
                excludedIds.Remove(id);
                if (!points.TryGetValue(id, out MapListingPoint? point)) { invalid++; errors.Add("INVALID_MAP_ID_OR_URL"); continue; }
                string[] photos = Photos(Get(item, "images")).Concat(Photos(Get(item, "gallery"))).Distinct(StringComparer.Ordinal).ToArray();
                string? date = Get(Get(item, "iva"), "DateInfoStep").ValueKind == JsonValueKind.Array
                    ? Get(Get(item, "iva"), "DateInfoStep").EnumerateArray().Select(x => Text(Get(Get(x, "payload"), "relative"))).FirstOrDefault(x => x is not null) : null;
                JsonElement price = Get(item, "priceDetailed");
                DomCard card = new(id, new Uri(new Uri("https://www.avito.ru"), Text(Get(item, "urlPath"))).AbsoluteUri,
                    Text(Get(item, "title")), Text(Get(price, "fullString")) ?? Text(Get(price, "string")) ?? Text(Get(price, "value")), null,
                    Text(Get(Get(item, "geo"), "formattedAddress")) ?? Text(Get(Get(item, "addressDetailed"), "locationName")), null,
                    Text(Get(item, "description")), date, null, null, null, [], photos);
                ListingObservation? listing = DomSourcePage.Parse(new("SearchResults", [card], []), SourceSite.Avito, time).Listings.FirstOrDefault();
                if (listing is null) { invalidCards++; errors.Add("INVALID_MAP_CARD"); continue; }
                listing = listing with { AdapterVersion = "1.1-map", Provenance = "AvitoMapJSON",
                    Latitude = NumberValue.Read(point.Latitude?.ToString(CultureInfo.InvariantCulture)),
                    Longitude = NumberValue.Read(point.Longitude?.ToString(CultureInfo.InvariantCulture)),
                    CoordinatePrecision = NumberValue.Read(point.Precision?.ToString(CultureInfo.InvariantCulture)) };
                parsedPoints.Add(point);
                changes.Add(listing);
                if (!items.TryGetValue(id, out ListingObservation? prior) || prior.ObservedAtUtc <= time) items[id] = listing;
            }
            diagnosticPoints.Add(parsedPoints.ToArray());
            batchDiagnostics.Add(new(Batches, received, newIds, repeats, recommendations, invalid, invalidCards, 0, 0, batchIds.ToArray()));
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or FormatException)
        { errors.Add("MAP_JSON_INVALID_" + ex.GetType().Name); }
    }
    public void Reject(int status)
    { if (status == 429) Kind = PageKind.RateLimited; else if (status >= 400) Kind = PageKind.SourceError; }
    private readonly Dictionary<string, ListingObservation> dom = new(StringComparer.Ordinal);
    public void Enrich(IEnumerable<ListingObservation> observations)
    { foreach (ListingObservation observation in observations) dom[observation.ExternalId] = observation; }
    private ListingObservation Enriched(ListingObservation listing)
    {
        if (!dom.TryGetValue(listing.ExternalId, out ListingObservation? card)) return listing;
        static TextValue Prefer(TextValue original, TextValue fallback) => original.Raw is null ? fallback : original;
        return listing with { SellerName = card.SellerName, SellerUrl = card.SellerUrl, SellerType = card.SellerType,
            SellerStatistics = card.SellerStatistics, CompletedAdvertisements = card.CompletedAdvertisements,
            Badges = card.Badges, UnitPrice = card.UnitPrice, Transport = card.Transport,
            Description = Prefer(listing.Description, card.Description), DateText = Prefer(listing.DateText, card.DateText) };
    }
    public PageObservation Read(string url, bool loading, string? expectedDrawId = null)
    {
        string? currentDraw = DrawId(url);
        string? draw = expectedDrawId ?? currentDraw;
        bool needsDraw = draw is not null;
        bool drawMatches = expectedDrawId is null || expectedDrawId == currentDraw;
        bool confirmed = !needsDraw || (drawMatches && rings.Length > 0 && zoneError is null);
        MapScope scope = new(draw, needsDraw, confirmed, rings, expected, Batches,
            !drawMatches ? "Изменился идентификатор области; требуется сравнение геометрии" : needsDraw ? zoneError : null,
            observedAreas, geometryRequestDraw);
        bool Include(ListingObservation item)
        {
            if (!confirmed) return false;
            if (!needsDraw) return true;
            return SearchAreaGeometry.Contains(observedAreas, item);
        }
        List<string> warnings = new(errors);
        if (confirmed && needsDraw && items.Values.Any(x => x.Latitude.Parsed is null || x.Longitude.Parsed is null)) warnings.Add("MAP_COORDINATES_MISSING");
        ListingObservation[] updates = confirmed ? changes.Where(Include).Select(Enriched).ToArray() : [];
        if (confirmed) changes.Clear();
        ListingObservation[] collected = items.Values.Where(Include).Select(Enriched).ToArray();
        HashSet<string> kept = collected.Select(x => x.ExternalId).ToHashSet(StringComparer.Ordinal);
        string[] missing = items.Values.Where(x => x.Latitude.Parsed is null || x.Longitude.Parsed is null).Select(x=>x.ExternalId).Order(StringComparer.Ordinal).ToArray();
        string[] outside = confirmed && needsDraw ? items.Values.Where(x => x.Latitude.Parsed is decimal lat && x.Longitude.Parsed is decimal lon
            && !SearchAreaGeometry.Contains(observedAreas,new GeoPoint(lon,lat))).Select(x=>x.ExternalId).Order(StringComparer.Ordinal).ToArray() : [];
        MapDiagnostic diagnostic = new(batchDiagnostics.Select((b,index)=>b with {
            MissingCoordinates=diagnosticPoints[index].Count(p=>p.Latitude is null || p.Longitude is null),
            OutsidePolygon=confirmed && needsDraw ? diagnosticPoints[index].Count(p=>p.Latitude is decimal lat && p.Longitude is decimal lon && !SearchAreaGeometry.Contains(observedAreas,new GeoPoint(lon,lat))) : 0 }).ToArray(), expected, receivedIds.Count, collected.Length,
            missing, outside, dom.Keys.Order(StringComparer.Ordinal).ToArray(), dom.Keys.Where(x=>!receivedIds.Contains(x)).Order(StringComparer.Ordinal).ToArray(),
            dom.Keys.Where(x=>!kept.Contains(x)).Order(StringComparer.Ordinal).ToArray(),confirmed,loading);
        return new(Kind, collected, warnings.ToArray(), loading,
            "AVITO_MAP:" + Batches, scope, updates, diagnostic, UnverifiedListings:
                items.Values.Select(Enriched).Concat(dom.Values.Where(item => !items.ContainsKey(item.ExternalId) && !excludedIds.Contains(item.ExternalId))).ToArray(),
            ExcludedExternalIds: excludedIds.ToArray());
    }
    public static string? DrawId(string url)
    {
        string? value = new Uri(url).Query.TrimStart('?').Split('&').FirstOrDefault(x => x.StartsWith("drawId=", StringComparison.Ordinal));
        return value is null ? null : Uri.UnescapeDataString(value[7..]);
    }
    /// <summary>GeoJSON longitude first; holes exclude their interior. Outer boundary is included.</summary>
    public static bool Contains(GeoPoint[][] rings, GeoPoint point) => rings.Length > 0 && InRing(rings[0], point)
        && !rings.Skip(1).Any(r => InRing(r, point));
    private static bool InRing(GeoPoint[] ring, GeoPoint p)
    {
        bool inside = false;
        for (int i = 1; i < ring.Length; i++)
        {
            GeoPoint a = ring[i - 1], b = ring[i];
            decimal cross = (p.Longitude - a.Longitude) * (b.Latitude - a.Latitude) - (p.Latitude - a.Latitude) * (b.Longitude - a.Longitude);
            if (Math.Abs(cross) < 0.000000000001m && p.Longitude >= Math.Min(a.Longitude,b.Longitude) && p.Longitude <= Math.Max(a.Longitude,b.Longitude)
                && p.Latitude >= Math.Min(a.Latitude,b.Latitude) && p.Latitude <= Math.Max(a.Latitude,b.Latitude)) return true;
            if ((a.Latitude > p.Latitude) != (b.Latitude > p.Latitude)
                && p.Longitude < (b.Longitude-a.Longitude)*(p.Latitude-a.Latitude)/(b.Latitude-a.Latitude)+a.Longitude) inside = !inside;
        }
        return inside;
    }
    private static JsonElement Get(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out JsonElement field) ? field : default;
    private static string? Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() : value.ValueKind == JsonValueKind.Number ? value.GetRawText() : null;
    private static bool True(JsonElement value) => value.ValueKind == JsonValueKind.True;
    // Traverse only public picture containers, never the response's user/auth fields.
    // A size dictionary describes one photograph, not several independent photographs.
    private static IEnumerable<string> Photos(JsonElement value, int depth = 0)
    {
        if (depth > 8) yield break;
        if (value.ValueKind == JsonValueKind.Object)
        {
            var sizes = value.EnumerateObject().Select(property =>
            {
                string[] dimensions = property.Name.Split('x');
                long area = dimensions.Length == 2 && int.TryParse(dimensions[0], out int width)
                    && int.TryParse(dimensions[1], out int height) && width > 0 && height > 0
                    ? (long)width * height : 0;
                return (Area: area, Value: property.Value);
            }).Where(item => item.Area > 0 && item.Value.ValueKind == JsonValueKind.String)
                .OrderByDescending(item => item.Area).ToArray();
            if (sizes.Length > 0)
            {
                foreach (string url in Strings(sizes[0].Value)) yield return url;
                yield break;
            }
            foreach (JsonProperty child in value.EnumerateObject().Take(500))
                foreach (string url in Photos(child.Value, depth + 1)) yield return url;
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in value.EnumerateArray().Take(500))
                foreach (string url in Photos(child, depth + 1)) yield return url;
        }
        else foreach (string url in Strings(value, depth)) yield return url;
    }

    private static IEnumerable<string> Strings(JsonElement value, int depth = 0)
    {
        if (depth > 8) yield break;
        if (value.ValueKind == JsonValueKind.String) { string? text = value.GetString(); if (text is not null) yield return text; }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (JsonElement child in value.EnumerateArray().Take(500)) foreach (string text in Strings(child, depth+1)) yield return text;
        else if (value.ValueKind == JsonValueKind.Object)
            foreach (JsonProperty child in value.EnumerateObject().Take(500)) foreach (string text in Strings(child.Value, depth+1)) yield return text;
    }
}

internal sealed class AvitoMapCapture : IDisposable
{
    private readonly IPage page;
    private readonly object sync = new();
    private AvitoMapData data = new();
    private int generation, pending;
    private bool disposed;
    private string? expectedDraw;
    public AvitoMapCapture(IPage page) { this.page = page; page.Response += Response; }
    public void Reset(string url) { lock (sync) { generation++; pending = 0; data = new(); expectedDraw = AvitoMapData.DrawId(url); } }
    private async void Response(object? sender, IResponse response)
    {
        if (!Uri.TryCreate(response.Url, UriKind.Absolute, out Uri? uri) || !SearchUrls.IsPublic(uri, SourceSite.Avito)
            || uri.Host is not ("avito.ru" or "www.avito.ru") || uri.AbsolutePath is not ("/web/1/map/markers" or "/web/1/map/pins" or "/js/1/map/items")) return;
        int epoch; DateTimeOffset time = DateTimeOffset.UtcNow;
        lock (sync)
        {
            if (disposed) return;
            epoch = generation;
            if (response.Status >= 400) { data.Reject(response.Status); return; }
            // Page limits do not bound dynamic map responses. The runner bounds scrolling;
            // every response already triggered by the browser must remain eligible for capture.
            pending++;
        }
        try
        {
            byte[] bytes = await response.BodyAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            lock (sync)
            {
                if (disposed || generation != epoch) return;
                if (bytes.Length > 4 * 1024 * 1024) data.Reject(413);
                else data.Ingest(uri.GetLeftPart(UriPartial.Path), System.Text.Encoding.UTF8.GetString(bytes), time, RequestDrawId(response.Request));
            }
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException or InvalidOperationException)
        { lock (sync) if (!disposed && epoch == generation) data.Reject(500); }
        finally { lock (sync) if (epoch == generation) pending--; }
    }
    private static string? RequestDrawId(IRequest request)
    {
        string[] drawTerms = new Uri(request.Url).Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(term => Uri.UnescapeDataString(term.Split('=', 2)[0]) == "drawId").ToArray();
        if (drawTerms.Length > 1) return null;
        if (drawTerms.Length == 1)
        {
            string[] pair = drawTerms[0].Split('=', 2);
            string value = pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : "";
            return value.Length is > 0 and <= 200 && !value.Any(char.IsControl) ? value : null;
        }
        if (request.PostData is not { Length: <= 65536 } body) return null;
        try
        {
            using JsonDocument json = JsonDocument.Parse(body);
            JsonElement root = json.RootElement;
            return root.ValueKind == JsonValueKind.Object && root.EnumerateObject().Count(property => property.Name == "drawId") == 1
                && root.TryGetProperty("drawId", out JsonElement id) && id.ValueKind == JsonValueKind.String
                && id.GetString() is { Length: > 0 and <= 200 } value && !value.Any(char.IsControl) ? value : null;
        }
        catch (JsonException) { return null; }
    }
    public PageObservation Read(string url, bool loading, ListingObservation[] dom) { lock (sync) { data.Enrich(dom); return data.Read(url, loading || pending > 0, expectedDraw); } }
    public void Dispose() { lock (sync) { disposed = true; generation++; } page.Response -= Response; }
}
