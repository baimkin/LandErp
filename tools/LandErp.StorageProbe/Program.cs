using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Playwright;
using LandErp.StorageProbe;

// Only this synthetic probe is authorized. No application DB or existing object files are read.
// Never print exceptions, OAuth headers, config content, upload URLs or browser network events.
var result = new ProbeResult();
// Hard deadline also covers a stuck browser driver/disposal. Only sanitized state is emitted.
using var deadline = new Timer(_ =>
{
    result.Outcome = "INCONCLUSIVE"; result.Code = "PROBE_DEADLINE_EXCEEDED";
    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    Environment.Exit(3);
}, null, TimeSpan.FromSeconds(args.Contains("--self-test", StringComparer.Ordinal) ? 45 : 180), Timeout.InfiniteTimeSpan);
try
{
    if (args.Contains("--self-test", StringComparer.Ordinal)) return await ProbeSelfTest.RunAsync(stage => result.Stage = stage);
    string? Argument(string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
    string? token, root, originText = Argument("--origin");
    if (args.Contains("--from-env", StringComparer.Ordinal))
    {
        token = Environment.GetEnvironmentVariable("Storage__YandexDisk__Token");
        root = Environment.GetEnvironmentVariable("Storage__YandexDisk__Root");
    }
    else
    {
        string configPath = Argument("--config") ?? @"C:\ProgramData\LandErp\config\production.json";
        using var config = JsonDocument.Parse(await File.ReadAllTextAsync(configPath));
        var storage = config.RootElement.GetProperty("Storage");
        if (storage.GetProperty("Provider").GetString() != "YandexDisk") throw new ProbeFailure("NOT_YANDEX_DISK");
        var disk = storage.GetProperty("YandexDisk");
        token = disk.GetProperty("Token").GetString();
        root = disk.GetProperty("Root").GetString();
        string[] hosts = (config.RootElement.GetProperty("AllowedHosts").GetString() ?? "").Split(';');
        originText ??= hosts.Select(x => x.Trim()).Where(x => x.Length > 0 && !x.Contains('*')
            && !x.Equals("localhost", StringComparison.OrdinalIgnoreCase) && x != "127.0.0.1" && x != "[::1]")
            .Select(x => "https://" + x).FirstOrDefault();
    }
    if (string.IsNullOrWhiteSpace(token)) throw new ProbeFailure("TOKEN_UNAVAILABLE");
    if (root == null || !root.StartsWith("app:/LandErp/", StringComparison.Ordinal)
        || root.Length > 100 || root[5..].Split('/').Any(x => x.Length == 0 || x is "." or ".."
            || !x.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
        throw new ProbeFailure("INVALID_APP_ROOT");
    if (!Uri.TryCreate(originText, UriKind.Absolute, out var origin) || origin.Scheme != "https"
        || origin.UserInfo.Length > 0 || origin.AbsolutePath != "/" || origin.Query.Length > 0 || origin.Fragment.Length > 0)
        throw new ProbeFailure("PUBLIC_HTTPS_ORIGIN_REQUIRED");
    result.Origin = origin.GetLeftPart(UriPartial.Authority);
    result.CheckId = Guid.NewGuid().ToString("N");
    byte[] bytes = Encoding.UTF8.GetBytes("LandErp direct-upload probe " + result.CheckId);
    result.FileBytes = bytes.Length;
    result.Stage = "BrowserStart";
    using var playwright = await Playwright.CreateAsync().WaitAsync(TimeSpan.FromSeconds(30));
    await using var browser = await playwright.Chromium.LaunchAsync(new()
        { Channel = Argument("--browser") ?? "msedge", Headless = true, ChromiumSandbox = true, Timeout = 30000 });
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    // Isolated synthetic page at the public site's origin. This does not contact/modify the ERP
    // and does NOT intercept the external upload request or bypass normal browser CORS checks.
    string pageUrl = result.Origin + "/.landerp-storage-probe-" + result.CheckId;
    await page.RouteAsync(pageUrl, route => route.FulfillAsync(new() { ContentType = "text/html", Body = "<!doctype html><title>LandErp storage probe</title>" }));
    await page.GotoAsync(pageUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded });

    using var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false };
    using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    var diskApi = new ProbeDiskApi(http, token);
    result.Stage = "PrepareScratchFile";
    string directory = root + "/Checks/" + result.CheckId;
    await diskApi.EnsureDirectoryAsync(directory);
    string path = directory + "/direct-probe.txt";
    Uri upload = await diskApi.UploadLinkAsync(path);
    result.Stage = "Preflight";
    using var options = new HttpRequestMessage(HttpMethod.Options, upload);
    options.Headers.Add("Origin", result.Origin);
    options.Headers.Add("Access-Control-Request-Method", "PUT");
    options.Headers.Add("Access-Control-Request-Headers", "content-type");
    using var preflight = await http.SendAsync(options);
    result.PreflightStatus = (int)preflight.StatusCode;
    result.AllowOrigin = Cors.AllowsOrigin(preflight, result.Origin);
    result.AllowPut = Cors.HasToken(preflight, "Access-Control-Allow-Methods", "PUT");
    result.AllowContentType = Cors.HasToken(preflight, "Access-Control-Allow-Headers", "content-type");
    result.Stage = "BrowserPut";
    var browserUpload = await BrowserPut.RunAsync(page, upload, bytes);
    result.BrowserEvent = browserUpload.Event;
    result.BrowserStatus = browserUpload.Status;
    result.ProgressObserved = browserUpload.ProgressObserved;
    if (browserUpload.Event == "load" && browserUpload.Status is >= 200 and < 300)
    {
        result.Stage = "ProviderConfirmation";
        result.ProviderVerified = await diskApi.VerifyAsync(path, bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        result.Outcome = result.ProviderVerified ? "DIRECT_SUPPORTED" : "INCONCLUSIVE";
        result.Code = result.ProviderVerified ? "BROWSER_PUT_AND_HASH_CONFIRMED" : "PROVIDER_CONFIRMATION_PENDING";
    }
    else if (browserUpload.Event == "error" && result.PreflightStatus is >= 200 and < 500 and not 429
        && !(result.AllowOrigin && result.AllowPut && result.AllowContentType))
    {
        result.Outcome = "DIRECT_BLOCKED_CORS";
        result.Code = "FRESH_UPLOAD_URL_REJECTS_BROWSER_PREFLIGHT";
    }
    else { result.Code = "BROWSER_UPLOAD_NOT_CONFIRMED"; }
}
catch (ProbeFailure ex) { result.Code = ex.Code; }
catch (Exception ex) { result.Code = "PROBE_" + ex.GetType().Name.ToUpperInvariant(); }
Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
return result.Outcome == "DIRECT_SUPPORTED" ? 0 : result.Outcome == "DIRECT_BLOCKED_CORS" ? 2 : 3;