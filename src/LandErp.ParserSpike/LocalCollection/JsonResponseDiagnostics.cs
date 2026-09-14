using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace LandErp.ParserSpike.LocalCollection;

public sealed record JsonResponseEvent(DateTimeOffset TimeUtc, SourceSite Source, string Endpoint,
    int Status, int Bytes, string Outcome, JsonResponseShape? Shape, MapResponseSummary? Map = null);
public sealed record JsonResponseShape(string Type, object? Value = null, int? Count = null,
    Dictionary<string, JsonResponseShape>? Fields = null, JsonResponseShape[]? Samples = null);
public sealed record MapListingPoint(string Id, decimal? Latitude, decimal? Longitude, decimal? Precision);
public sealed record MapMarkerPoint(string? MarkerId, int? ItemsCount, decimal? Latitude, decimal? Longitude, bool? IsOutGeo);
public sealed record MapResponseSummary(int? TotalCount, MapListingPoint[] Listings, MapMarkerPoint[] Markers,
    string DrawOutcome, JsonResponseShape? DrawGeometry);

/// <summary>Opt-in response inspection, never a request replay or a raw network dump.
/// Unknown values are represented by types; private branches, headers and HTML are excluded.</summary>
public sealed class JsonResponseDiagnostics(string directory) : IAsyncDisposable
{
    private readonly object sync = new();
    private readonly SemaphoreSlim slots = new(4, 4);
    private readonly CancellationTokenSource shutdown = new();
    private readonly HashSet<Task> pending = [];
    private volatile bool enabled;
    private int recorded;
    private int dropped;
    private string? path;
    public bool Enabled => enabled;
    public string? Path { get { lock (sync) return path; } }
    public string Status { get { lock (sync) return $"{(enabled ? "Запись включена" : "Запись выключена")}; ответов {recorded}; пропущено {dropped}. Файл: {path ?? "ещё не создан"}"; } }
    public void Start()
    {
        lock (sync)
        {
            if (pending.Count != 0) throw new InvalidOperationException("Дождитесь завершения текущих JSON-ответов и повторите.");
            Directory.CreateDirectory(directory);
            path = System.IO.Path.Combine(directory, "browser-json-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8] + ".jsonl");
            File.WriteAllText(path, "");
            recorded = 0; dropped = 0; enabled = true;
        }
    }
    public void Stop() => enabled = false;
    public void Observe(IResponse response, SourceSite source)
    {
        // Hook the whole context, so manually opened tabs are inspected too.
        if (!enabled || !Uri.TryCreate(response.Url, UriKind.Absolute, out Uri? uri) || !SearchUrls.IsPublic(uri, source)
            || Regex.IsMatch(uri.AbsolutePath, @"messeng|chat|auth|login|account|profile|user|phone|contact|notification|call|payment|wallet|balance|token|session|passport|telemetr|analytics|tracking", RegexOptions.IgnoreCase)) return;
        // The Avito experiment now targets only the two responses confirmed by the owner's session.
        if (source == SourceSite.Avito && uri.AbsolutePath is not ("/js/1/map/items" or "/web/1/map/markers")) return;
        if (!response.Headers.TryGetValue("content-type", out string? mime)
            || !mime.Contains("json", StringComparison.OrdinalIgnoreCase)) return;
        lock (sync)
        {
            if (!enabled) return;
            if (!slots.Wait(0)) { dropped++; return; }
            string capturePath = path!;
            Task task = CaptureAsync(response, source, Endpoint(uri), capturePath);
            pending.Add(task);
            _ = task.ContinueWith(t => { lock (sync) pending.Remove(t); }, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
    private async Task CaptureAsync(IResponse response, SourceSite source, string endpoint, string capturePath)
    {
        try
        {
            string? contentType = await response.HeaderValueAsync("content-type").WaitAsync(TimeSpan.FromSeconds(3), shutdown.Token).ConfigureAwait(false);
            if (contentType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true) return;
            string? length = await response.HeaderValueAsync("content-length").WaitAsync(TimeSpan.FromSeconds(3), shutdown.Token).ConfigureAwait(false);
            if (long.TryParse(length, out long bytes) && bytes > 4 * 1024 * 1024)
            { Append(capturePath, new(DateTimeOffset.UtcNow, source, endpoint, response.Status, 0, "SkippedLargeBody", null)); return; }
            byte[] body = await response.BodyAsync().WaitAsync(TimeSpan.FromSeconds(5), shutdown.Token).ConfigureAwait(false);
            if (body.Length > 4 * 1024 * 1024)
            { Append(capturePath, new(DateTimeOffset.UtcNow, source, endpoint, response.Status, body.Length, "SkippedLargeBody", null)); return; }
            using JsonDocument document = JsonDocument.Parse(body, new() { MaxDepth = 64 });
            int budget = 2000;
            JsonResponseShape shape = Inspect(document.RootElement, "", false, 0, ref budget);
            MapResponseSummary? map = source == SourceSite.Avito ? SummarizeMap(document.RootElement, endpoint) : null;
            Append(capturePath, new(DateTimeOffset.UtcNow, source, endpoint, response.Status, body.Length, "JsonSchema", shape, map));
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException or JsonException or OperationCanceledException)
        {
            // Only exception type: raw browser messages can contain secrets.
            Append(capturePath, new(DateTimeOffset.UtcNow, source, endpoint, response.Status, 0, ex.GetType().Name, null));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { enabled = false; }
        finally { slots.Release(); }
    }
    private void Append(string capturePath, JsonResponseEvent entry)
    {
        lock (sync)
        {
            if (recorded >= 500) { enabled = false; dropped++; return; }
            try
            {
                File.AppendAllText(capturePath, JsonSerializer.Serialize(entry) + Environment.NewLine);
                recorded++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { enabled = false; dropped++; }
        }
    }
    private static string Endpoint(Uri uri)
    {
        // Strip every query value, and avoid persisting opaque IDs in path segments.
        string path = string.Join('/', uri.AbsolutePath.Split('/').Select(s =>
            s.Length > 24 || s.Contains('%') || Regex.IsMatch(s, @"^\d{5,}$|^[a-fA-F0-9]{24,}$") ? "{id}" : s));
        return uri.GetLeftPart(UriPartial.Authority) + path;
    }
    private static bool Private(string key) => Regex.IsMatch(key,
        @"user|account|profile|auth|token|cookie|session|password|secret|csrf|jwt|phone|contact|messeng|chat|email|balance|wallet|avatar|experiment|abcentral|toggle|tracking|telemetr|analytics|headers|seller|owner|customer|employee|passport|permission|signature|credential|device|fingerprint|hash|apikey|accesskey|bearer",
        RegexOptions.IgnoreCase);
    private static JsonResponseShape Inspect(JsonElement value, string key, bool listing, int depth, ref int budget)
    {
        if (--budget < 0 || depth > 12) return new("Truncated");
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                // Preserve an ID only when this object has explicit evidence of being a public listing.
                bool item = value.EnumerateObject().Any(p =>
                    p.Name is "itemId" or "offerId" or "externalId"
                    || (p.Name is "url" or "href" or "urlPath" && p.Value.ValueKind == JsonValueKind.String
                        && Regex.IsMatch(p.Value.GetString() ?? "", @"_\d{5,}(?:\?|$)|/sale/[^/]+/\d{5,}")));
                Dictionary<string, JsonResponseShape> fields = [];
                int count = 0;
                foreach (JsonProperty property in value.EnumerateObject())
                {
                    if (count++ >= 64 || budget <= 0) { fields["__truncated__"] = new("Truncated"); break; }
                    if (Private(property.Name)) continue;
                    string name = Regex.IsMatch(property.Name, @"^@?[A-Za-z_$][A-Za-z0-9_$]{0,79}$|^\d{1,3}$") ? property.Name : "__dynamic_key__";
                    fields[name] = Inspect(property.Value, name, item, depth + 1, ref budget);
                }
                return new("Object", Count: count, Fields: fields);
            case JsonValueKind.Array:
                List<JsonResponseShape> samples = [];
                foreach (JsonElement element in value.EnumerateArray().Take(3))
                    samples.Add(Inspect(element, key, listing, depth + 1, ref budget));
                return new("Array", Count: value.GetArrayLength(), Samples: samples.ToArray());
            case JsonValueKind.Number:
                string lower = key.ToLowerInvariant();
                object? number = null;
                if (value.TryGetDecimal(out decimal numeric)
                    && ((lower is "lat" or "latitude" or "latbottom" or "lattop" && Math.Abs(numeric) <= 90)
                    || (lower is "lon" or "lng" or "longitude" or "lonleft" or "lonright" && Math.Abs(numeric) <= 180)
                    || (lower is "coordinates" or "coords" && Math.Abs(numeric) <= 180)
                    || (lower is "count" or "total" or "totalcount" or "itemscount" or "page" or "zoom" && numeric >= 0 && numeric <= 100000000)
                    || (listing && lower is "id" or "itemid" or "offerid" or "externalid" && numeric >= 10000 && numeric <= 99999999999999999999m)))
                    number = numeric;
                return new("Number", number);
            case JsonValueKind.String:
                string text = value.GetString() ?? "";
                object? safe = key == "drawId" && Regex.IsMatch(text, "^[a-fA-F0-9]{32}$") ? text : null;
                if (listing && key is "id" or "itemId" or "offerId" or "externalId" && Regex.IsMatch(text, @"^\d{5,20}$")) safe = text;
                if (key is "status" or "code" or "errorCode" && text is "OK" or "SUCCESS" or "ERROR" or "NOT_FOUND" or "DRAW_NOT_FOUND" or "DRAW_EXPIRED" or "INVALID_DRAW_ID" or "EXPIRED" or "FAILED") safe = text;
                return new("String", safe);
            case JsonValueKind.True:
            case JsonValueKind.False: return new("Boolean", value.GetBoolean());
            default: return new("Null");
        }
    }
    /// <summary>Only known public map endpoints may export complete ID/coordinate summaries.
    /// Geometry decoding success is not evidence that the source applied a selected zone.</summary>
    public static MapResponseSummary? SummarizeMap(JsonElement root, string endpoint)
    {
        if (root.ValueKind != JsonValueKind.Object || !Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri)
            || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0
            || uri.Host is not ("www.avito.ru" or "avito.ru")) return null;
        List<MapListingPoint> listings = [];
        List<MapMarkerPoint> markers = [];
        if (uri.AbsolutePath == "/js/1/map/items")
        {
            if (root.TryGetProperty("items", out JsonElement items) && items.ValueKind == JsonValueKind.Array)
                foreach (JsonElement item in items.EnumerateArray().Take(5000))
                {
                    string? id = Id(item);
                    if (id is null || !item.TryGetProperty("urlPath", out JsonElement link)
                        || link.ValueKind != JsonValueKind.String
                        || !Uri.TryCreate(new Uri("https://www.avito.ru"), link.GetString(), out Uri? listingUrl)
                        || !SearchUrls.IsPublic(listingUrl, SourceSite.Avito)
                        || !Regex.IsMatch(listingUrl.AbsolutePath, "_" + id + "/?$")) continue;
                    JsonElement coords = Property(item, "coords");
                    (decimal? lat, decimal? lng) = Point(coords);
                    listings.Add(new(id, lat, lng, Range(Property(coords, "precision"), 0, 1000000)));
                }
            return new(Integer(Property(root, "totalCount")), listings.ToArray(), [], "NotMarkerResponse", null);
        }
        if (uri.AbsolutePath != "/web/1/map/markers") return null;
        if (root.TryGetProperty("markers", out JsonElement pins) && pins.ValueKind == JsonValueKind.Array)
            foreach (JsonElement pin in pins.EnumerateArray().Take(5000))
            {
                if (pin.ValueKind != JsonValueKind.Object) continue;
                (decimal? lat, decimal? lng) = Point(Property(pin, "coords"));
                JsonElement count = Property(pin, "itemsCount"), outside = Property(pin, "isOutGeo");
                // Marker IDs may identify clusters, so never call them listing IDs.
                markers.Add(new(Id(pin), Integer(count), lat, lng,
                    outside.ValueKind is JsonValueKind.True or JsonValueKind.False ? outside.GetBoolean() : null));
            }
        JsonElement encoded = Property(root, "drawAreaBase64");
        (string outcome, JsonResponseShape? geometry) = DecodeGeometry(encoded);
        return new(null, [], markers.ToArray(), outcome, geometry);
    }
    private static JsonElement Property(JsonElement value, string key) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out JsonElement property) ? property : default;
    private static string? Id(JsonElement value)
    {
        JsonElement property = Property(value, "id");
        string? text = property.ValueKind == JsonValueKind.String ? property.GetString()
            : property.ValueKind == JsonValueKind.Number ? property.GetRawText() : null;
        return text is not null && Regex.IsMatch(text, @"^\d{5,20}$") ? text : null;
    }
    private static decimal? Range(JsonElement value, decimal min, decimal max)
    {
        decimal number;
        bool parsed = value.ValueKind == JsonValueKind.Number ? value.TryGetDecimal(out number)
            : decimal.TryParse(value.ValueKind == JsonValueKind.String ? value.GetString() : null,
                NumberStyles.Float, CultureInfo.InvariantCulture, out number);
        return parsed && number >= min && number <= max ? number : null;
    }
    private static int? Integer(JsonElement value)
    {
        decimal? number = Range(value, 0, int.MaxValue);
        return number.HasValue && decimal.Truncate(number.Value) == number.Value ? (int)number.Value : null;
    }
    private static (decimal? Latitude, decimal? Longitude) Point(JsonElement value)
    {
        decimal? lat = Range(Property(value, "lat"), -90, 90);
        JsonElement longitude = Property(value, "lng");
        if (longitude.ValueKind == JsonValueKind.Undefined) longitude = Property(value, "lon");
        decimal? lng = Range(longitude, -180, 180);
        // Incomplete/invalid pairs can't place an object on a map.
        return lat.HasValue && lng.HasValue ? (lat, lng) : (null, null);
    }
    private static (string Outcome, JsonResponseShape? Shape) DecodeGeometry(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return ("Absent", null);
        if (value.ValueKind != JsonValueKind.String) return ("UnexpectedType", null);
        string encoded = value.GetString() ?? "";
        if (encoded.Length == 0) return ("Empty", null);
        if (encoded.Length > 512 * 1024) return ("SkippedLargeGeometry", null);
        try
        {
            string input = Uri.UnescapeDataString(encoded).Replace('-', '+').Replace('_', '/');
            byte[] bytes = Convert.FromBase64String(input.PadRight(input.Length + (4 - input.Length % 4) % 4, '='));
            string json = new UTF8Encoding(false, true).GetString(bytes);
            using JsonDocument document = JsonDocument.Parse(json, new() { MaxDepth = 64 });
            int budget = 12000;
            JsonResponseShape geometry = Geometry(document.RootElement, 0, ref budget);
            return (budget < 0 ? "DecodedJsonTruncated" : "DecodedJson", geometry);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or DecoderFallbackException)
        { return ("CannotDecode_" + ex.GetType().Name, null); }
    }
    private static JsonResponseShape Geometry(JsonElement value, int depth, ref int budget)
    {
        if (--budget < 0 || depth > 16) return new("Truncated");
        if (value.ValueKind == JsonValueKind.Object)
        {
            Dictionary<string, JsonResponseShape> fields = [];
            foreach (JsonProperty property in value.EnumerateObject())
            {
                if (budget < 0) { fields["__truncated__"] = new("Truncated"); break; }
                if (!Regex.IsMatch(property.Name, @"^(?:type|geometry|geometries|features|coordinates|coords|points|polygon|polygons|bbox|bounds|area|searchArea|drawArea|lat|lng|lon|latitude|longitude|latBottom|latTop|lonLeft|lonRight|data|result)$", RegexOptions.IgnoreCase)) continue;
                fields[property.Name] = Geometry(property.Value, depth + 1, ref budget);
            }
            return new("Object", Fields: fields);
        }
        if (value.ValueKind == JsonValueKind.Array)
        {
            List<JsonResponseShape> points = [];
            foreach (JsonElement element in value.EnumerateArray())
            {
                if (budget < 0) { points.Add(new("Truncated")); break; }
                points.Add(Geometry(element, depth + 1, ref budget));
            }
            return new("Array", Count: value.GetArrayLength(), Samples: points.ToArray());
        }
        if (value.ValueKind is JsonValueKind.Number or JsonValueKind.String)
        {
            decimal? number = Range(value, -180, 180);
            if (number.HasValue) return new("Number", number);
            if (value.ValueKind == JsonValueKind.String && value.GetString() is
                "Polygon" or "MultiPolygon" or "Feature" or "FeatureCollection" or "Point" or "MultiPoint" or "LineString" or "MultiLineString")
                return new("String", value.GetString());
            return new(value.ValueKind.ToString());
        }
        return new("Null");
    }
    public string Read()
    {
        lock (sync)
        {
            if (path is null) return Status;
            // Bounded session file and tail keep manual diagnostics out of the collection hot path.
            return Status + Environment.NewLine + string.Join(Environment.NewLine, File.ReadLines(path).TakeLast(100));
        }
    }
    public async ValueTask DisposeAsync()
    {
        enabled = false; await shutdown.CancelAsync().ConfigureAwait(false);
        Task[] tasks; lock (sync) tasks = pending.ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(false);
        slots.Dispose(); shutdown.Dispose();
    }
}
