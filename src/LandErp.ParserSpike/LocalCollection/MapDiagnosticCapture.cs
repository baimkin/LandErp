using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace LandErp.ParserSpike.LocalCollection;

/// <summary>Passive evidence only. Never produces observations or asserts that a canvas contour is visible.</summary>
public sealed class MapDiagnosticCapture : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly IPage page;
    private readonly object sync = new();
    private readonly HashSet<Task> pending = [];
    private readonly DateTimeOffset started = DateTimeOffset.UtcNow;
    private int responses, skipped, stopped;
    private long written;
    public string DirectoryPath { get; }
    public MapDiagnosticCapture(IPage page, string directory)
    {
        this.page = page; DirectoryPath = directory;
        Directory.CreateDirectory(directory);
        WriteStatus("Capturing");
        page.Response += OnResponse;
    }
    public static string CreateDirectory(string root)
    {
        string path = Path.Combine(root, DateTimeOffset.Now.ToString("yyyy-MM-dd_HH-mm-ss",
            System.Globalization.CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path); return path;
    }
    // Query values, opaque path parts, credentials and fragments never enter the network journal.
    public static string? SafeEndpoint(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || uri.Scheme != "https" || !uri.IsDefaultPort
            || uri.UserInfo.Length > 0 || !(uri.Host == "avito.ru" || uri.Host.EndsWith(".avito.ru", StringComparison.OrdinalIgnoreCase))) return null;
        if (Regex.IsMatch(uri.AbsolutePath, "auth|login|account|profile|user|phone|contact|message|chat|payment|wallet|token|session|passport|telemetry|analytics|tracking", RegexOptions.IgnoreCase)) return null;
        return uri.GetLeftPart(UriPartial.Authority) + "/" + string.Join("/", uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => Regex.IsMatch(s, "^[a-zA-Z_-]{1,32}$|^[0-9]{1,2}$") ? s : "_opaque_"));
    }
    public static object DescribeJson(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json, new() { MaxDepth = 48 });
        int budget = 12000;
        return Shape(document.RootElement, false, 0, ref budget);
    }
    private static object Shape(JsonElement value, bool geometry, int depth, ref int budget, bool typesOnly = false)
    {
        if (--budget < 0 || depth > 16) return "Truncated";
        if (value.ValueKind == JsonValueKind.Object)
        {
            Dictionary<string, object> fields = [];
            foreach (JsonProperty property in value.EnumerateObject())
            {
                if (budget < 0) { fields["_truncated"] = true; break; }
                // Only schema keys, never arbitrary IDs or private branches. Unknown string values stay types.
                if (!Regex.IsMatch(property.Name, "^[a-zA-Z_][a-zA-Z_0-9]{0,55}$")
                    || Regex.IsMatch(property.Name, "token|secret|password|cookie|auth|session|phone|contact|message|seller|profile|user", RegexOptions.IgnoreCase)) continue;
                bool geo = geometry || Regex.IsMatch(property.Name, "^(drawArea|searchArea|selectedArea|polygon|polygons|geometry|geometries|coordinates|contour)$", RegexOptions.IgnoreCase);
                if (!typesOnly && property.NameEquals("drawArea") && property.Value.ValueKind == JsonValueKind.String)
                {
                    object? structure = null;
                    var (outcome, decodedGeometry) = JsonResponseDiagnostics.DecodeGeometry(property.Value, allowJson: true,
                        describeDecoded: decoded =>
                        {
                            int structureBudget = 256;
                            structure = Shape(decoded, false, 0, ref structureBudget, typesOnly: true);
                        });
                    int polygonCount = SearchAreaGeometry.Read(decodedGeometry, out string? rejection).Length;
                    fields[property.Name] = new { Outcome = outcome, PolygonCount = polygonCount, Rejection = rejection, Structure = structure };
                }
                else if (!typesOnly && Regex.IsMatch(property.Name, "^(drawAreaBase64|geometryBase64)$", RegexOptions.IgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                {
                    string encoded = property.Value.GetString() ?? "";
                    if (encoded.Length > 512 * 1024) { fields[property.Name] = "TooLarge"; continue; }
                    try
                    {
                        encoded = Uri.UnescapeDataString(encoded).Replace('-', '+').Replace('_', '/');
                        using JsonDocument decoded = JsonDocument.Parse(Convert.FromBase64String(encoded.PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '=')), new() { MaxDepth = 32 });
                        fields[property.Name] = Shape(decoded.RootElement, true, depth + 1, ref budget);
                    }
                    catch (Exception ex) when (ex is FormatException or JsonException) { fields[property.Name] = "Undecodable"; }
                }
                else fields[property.Name] = Shape(property.Value, geo && !typesOnly, depth + 1, ref budget, typesOnly);
            }
            return fields;
        }
        if (value.ValueKind == JsonValueKind.Array)
        {
            List<object> items = [];
            foreach (JsonElement element in value.EnumerateArray().Take(geometry ? 4096 : 2))
            { if (budget < 0) break; items.Add(Shape(element, geometry, depth + 1, ref budget, typesOnly)); }
            return new { Kind = "Array", Count = value.GetArrayLength(), Samples = items };
        }
        if (typesOnly) return value.ValueKind is JsonValueKind.True or JsonValueKind.False ? "Boolean" : value.ValueKind.ToString();
        if (geometry && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out decimal n) && n >= -180 && n <= 180) return n;
        if (geometry && value.ValueKind == JsonValueKind.String && value.GetString() is "Polygon" or "MultiPolygon" or "Feature" or "FeatureCollection" or "LineString") return value.GetString()!;
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) return value.GetBoolean();
        return value.ValueKind.ToString();
    }
    private void OnResponse(object? sender, IResponse response)
    {
        string? endpoint = SafeEndpoint(response.Url);
        if (endpoint is null || response.Request.ResourceType is not ("fetch" or "xhr" or "document")) return;
        lock (sync)
        {
            if (stopped != 0) return;
            if (responses >= 160 || pending.Count >= 3 || DateTimeOffset.UtcNow - started > TimeSpan.FromMinutes(3) || written > 16 * 1024 * 1024) { skipped++; return; }
            responses++;
            Task task = CaptureAsync(response, endpoint); pending.Add(task);
            _ = task.ContinueWith(completed => { lock (sync) pending.Remove(completed); }, TaskScheduler.Default);
        }
    }
    private async Task CaptureAsync(IResponse response, string endpoint)
    {
        object? shape = null; string result = "MetadataOnly";
        try
        {
            string? mime = await response.HeaderValueAsync("content-type").WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            if (mime?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
            {
                string? size = await response.HeaderValueAsync("content-length").WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                if (long.TryParse(size, out long length) && length > 4 * 1024 * 1024) result = "TooLarge";
                else
                {
                    byte[] body = await response.BodyAsync().WaitAsync(TimeSpan.FromSeconds(4)).ConfigureAwait(false);
                    if (body.Length > 4 * 1024 * 1024) result = "TooLarge";
                    else { shape = DescribeJson(System.Text.Encoding.UTF8.GetString(body)); result = "SanitizedJson"; }
                }
            }
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException or JsonException or FormatException) { result = ex.GetType().Name; }
        string line = JsonSerializer.Serialize(new { AtUtc = DateTimeOffset.UtcNow, Endpoint = endpoint, response.Status, Result = result, Shape = shape }) + Environment.NewLine;
        lock (sync)
        {
            if (stopped == 2) return;
            if (written + line.Length > 16 * 1024 * 1024) { skipped++; return; }
            File.AppendAllText(Path.Combine(DirectoryPath, "network.jsonl"), line); written += line.Length;
        }
    }
    private void WriteStatus(string state) => File.WriteAllText(Path.Combine(DirectoryPath, "capture.json"), JsonSerializer.Serialize(new
    {
        State = state, StartedUtc = started, UpdatedUtc = DateTimeOffset.UtcNow,
        Responses = responses, Skipped = skipped, WrittenCharacters = written,
        RenderedOutline = "Unknown", ObservationsCreated = 0,
        Note = "Passive evidence; geometry, reset control and canvas are not proof of a visible contour. Compare screenshot."
    }, JsonOptions));
    public async ValueTask DisposeAsync()
    {
        Task[] work;
        lock (sync) { if (stopped != 0) return; stopped = 1; work = pending.ToArray(); }
        page.Response -= OnResponse;
        string state = "Stopped";
        try { await Task.WhenAll(work).WaitAsync(TimeSpan.FromSeconds(6)).ConfigureAwait(false); }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException) { state = ex.GetType().Name; }
        lock (sync) { stopped = 2; WriteStatus(state); }
    }
    public const string DomStateScript = """
        () => {
          const visible = e => {const r=e.getBoundingClientRect(),s=getComputedStyle(e);return r.width>0&&r.height>0&&s.display!=='none'&&s.visibility!=='hidden'&&Number(s.opacity)>0;};
          const box = e => {const r=e.getBoundingClientRect();return {visible:visible(e),x:r.x,y:r.y,width:r.width,height:r.height};};
          const maps=[...document.querySelectorAll('[data-marker*="map-view"],[data-marker*="map-full"],[data-marker*="map-container"]')].slice(0,16);
          return JSON.stringify({readyState:document.readyState,renderedOutline:'Unknown',
            maps:maps.map(box),canvas:[...document.querySelectorAll('canvas')].slice(0,16).map(box),
            resetAreaVisible:[...document.querySelectorAll('button,[role="button"]')].some(e=>visible(e)&&/сбросить.*област/i.test(e.textContent||'')),
            svg:[...document.querySelectorAll('svg')].filter(e=>maps.some(m=>m.contains(e))).slice(0,16).map(e=>({box:box(e),paths:[...e.querySelectorAll('path,polygon,polyline')].slice(0,24).map(p=>{
              const s=getComputedStyle(p),raw=p.getAttribute('d')||p.getAttribute('points')||'';
              return {box:box(p),stroke:s.stroke,strokeWidth:s.strokeWidth,fill:s.fill,
                path:raw.length<=24000&&/^[MmLlHhVvCcSsQqTtAaZz0-9eE.,+\s-]*$/.test(raw)?raw:null};})})),
            note:'No zoom, scroll, click or page state mutation; canvas visibility does not prove a contour.'});
        }
        """;
}
