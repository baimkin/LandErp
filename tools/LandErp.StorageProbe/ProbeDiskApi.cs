using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Playwright;

namespace LandErp.StorageProbe;

internal sealed class ProbeFailure(string code) : Exception { public string Code { get; } = code; }
internal sealed class ProbeResult
{
    public string Outcome { get; set; } = "INCONCLUSIVE";
    public string Stage { get; set; } = "Configuration";
    public string Code { get; set; } = "NOT_RUN";
    public string? Origin { get; set; }
    public string? CheckId { get; set; }
    public int FileBytes { get; set; }
    public int PreflightStatus { get; set; }
    public bool AllowOrigin { get; set; }
    public bool AllowPut { get; set; }
    public bool AllowContentType { get; set; }
    public string? BrowserEvent { get; set; }
    public int BrowserStatus { get; set; }
    public bool ProgressObserved { get; set; }
    public bool ProviderVerified { get; set; }
}
internal static class Cors
{
    public static bool AllowsOrigin(HttpResponseMessage response, string origin) =>
        response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values)
            && values.Any(v => v == "*" || v == origin);
    public static bool HasToken(HttpResponseMessage response, string header, string token) =>
        response.Headers.TryGetValues(header, out var values) && values.SelectMany(v => v.Split(','))
            .Any(v => v.Trim() == "*" || v.Trim().Equals(token, StringComparison.OrdinalIgnoreCase));
}
internal sealed class BrowserPutResult
{
    public string Event { get; set; } = "not-run";
    public int Status { get; set; }
    public bool ProgressObserved { get; set; }
}
internal static class BrowserPut
{
    public static Task<BrowserPutResult> RunAsync(IPage page, Uri upload, byte[] bytes) => page.EvaluateAsync<BrowserPutResult>("""
        ({url, bytes}) => new Promise(resolve => {
            const xhr = new XMLHttpRequest();
            let progress = false;
            const done = event => resolve({Event:event, Status:xhr.status, ProgressObserved:progress});
            xhr.open('PUT', url);
            xhr.timeout = 60000;
            xhr.setRequestHeader('Content-Type', 'application/octet-stream');
            xhr.upload.onprogress = () => { progress = true; };
            xhr.onload = () => done('load');
            xhr.onerror = () => done('error');
            xhr.ontimeout = () => done('timeout');
            xhr.onabort = () => done('abort');
            xhr.send(new Uint8Array(bytes));
        })
        """, new { url = upload.AbsoluteUri, bytes = bytes.Select(b => (int)b).ToArray() });
}
internal sealed class ProbeDiskApi(HttpClient http, string token)
{
    private const string Api = "https://cloud-api.yandex.net/v1/disk/resources";
    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string suffix, string path)
    {
        using var request = new HttpRequestMessage(method, Api + suffix + "?path=" + Uri.EscapeDataString(path) + (suffix == "/upload" ? "&overwrite=false" : ""));
        request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", token);
        return await http.SendAsync(request);
    }
    private static void Check(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode) throw new ProbeFailure("API_HTTP_" + (int)response.StatusCode);
    }
    public async Task EnsureDirectoryAsync(string path)
    {
        string current = "app:";
        foreach (string segment in path[5..].Split('/'))
        {
            current += "/" + segment;
            using var response = await SendAsync(HttpMethod.Put, "", current);
            if (response.StatusCode != HttpStatusCode.Conflict) Check(response);
        }
    }
    public async Task<Uri> UploadLinkAsync(string path)
    {
        using var response = await SendAsync(HttpMethod.Get, "/upload", path);
        Check(response);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        if (json.RootElement.GetProperty("method").GetString() != "PUT"
            || !Uri.TryCreate(json.RootElement.GetProperty("href").GetString(), UriKind.Absolute, out var upload)
            || upload.Scheme != "https" || !upload.IsDefaultPort || upload.UserInfo.Length > 0 || upload.Fragment.Length > 0
            || !(upload.Host.EndsWith(".yandex.net", StringComparison.OrdinalIgnoreCase) || upload.Host.EndsWith(".yandex.ru", StringComparison.OrdinalIgnoreCase)))
            throw new ProbeFailure("INVALID_UPLOAD_LINK");
        return upload;
    }
    public async Task<bool> VerifyAsync(string path, long length, string sha)
    {
        for (int attempt = 0; attempt < 15; attempt++)
        {
            using var response = await SendAsync(HttpMethod.Get, "", path);
            if (response.StatusCode != HttpStatusCode.NotFound)
            {
                Check(response);
                using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
                var root = json.RootElement;
                if (root.TryGetProperty("type", out var type) && type.GetString() == "file"
                    && root.TryGetProperty("size", out var size) && size.TryGetInt64(out long savedLength) && savedLength == length
                    && root.TryGetProperty("sha256", out var hash) && string.Equals(hash.GetString(), sha, StringComparison.OrdinalIgnoreCase)) return true;
            }
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
        return false;
    }
}