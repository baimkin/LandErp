using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using LandErp.Application.Foundation.Files;

namespace LandErp.Infrastructure.Foundation.Files;

/// <summary>Private app-folder storage. OAuth is sent only to the fixed API origin.
/// Download/upload URLs and raw provider errors must never escape this adapter.</summary>
public sealed class YandexDiskFileStorage : IFileStorage, IFileStorageHealth, IDisposable
{
    private const string Api = "https://cloud-api.yandex.net/v1/disk/resources";
    private readonly HttpClient http;
    private readonly YandexDiskOptions options;
    private readonly bool ownsClient;
    private readonly SemaphoreSlim transfers = new(4);

    public YandexDiskFileStorage(YandexDiskOptions options) : this(options, new HttpClient(new SocketsHttpHandler
    {
        AllowAutoRedirect = false, UseCookies = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    }) { Timeout = TimeSpan.FromSeconds(90) }, true) { }

    // Inject a handler-backed client for HTTP contract tests. Production uses the non-logging client above.
    public YandexDiskFileStorage(YandexDiskOptions options, HttpClient http) : this(options, http, false) { }

    private YandexDiskFileStorage(YandexDiskOptions options, HttpClient http, bool ownsClient)
    {
        options.Validate();
        this.options = options;
        this.http = http;
        this.ownsClient = ownsClient;
    }

    public Task<FileWriteResult> WriteAsync(Guid stableFileId, ReadOnlyMemory<byte> content, CancellationToken cancellationToken) =>
        WriteAsync(new FileWriteRequest(stableFileId, Guid.Empty, Guid.Empty, "Export", "application/octet-stream"), content, cancellationToken);

    public async Task<FileWriteResult> WriteAsync(FileWriteRequest request, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        if (request.FileId == Guid.Empty || content.Length == 0 || content.Length > options.MaxFileBytes)
            throw new ArgumentException("Файл должен иметь идентификатор и размер от 1 байта до установленного лимита.");
        string hash = Convert.ToHexStringLower(SHA256.HashData(content.Span));
        string category = request.Purpose switch
        {
            "Photo" => "Photos", "Document" => "Documents", "Video" => "Video", "Audio" => "Audio",
            "Export" => "Exports", _ => throw new ArgumentException("Неизвестное назначение файла.")
        };
        string directory = request.CaseId == Guid.Empty ? $"Checks/{request.FileId:N}"
            : $"Objects/{request.OrganizationId:N}/{request.CaseId:N}/{category}";
        // Content-addressed immutable names make a lost upload acknowledgement safe to retry,
        // without overwriting another version when the user picks a different file on retry.
        string relative = $"{directory}/{request.FileId:N}_{hash}{Extension(request.ContentType)}";
        string path = options.Root + "/" + relative;
        await transfers.WaitAsync(cancellationToken);
        try
        {
            await EnsureDirectoryAsync(options.Root + "/" + directory, cancellationToken);
            Resource? existing = await MetadataAsync(path, cancellationToken);
            if (existing != null)
            {
                Verify(existing, content.Length, hash);
                return new(Key(relative), hash, content.Length);
            }
            using JsonDocument link = await ApiJsonAsync(HttpMethod.Get, "/upload", path, "&overwrite=false", cancellationToken);
            Uri upload = TransferUri(link, "PUT");
            using HttpResponseMessage result = await SendAsync(() =>
            {
                HttpRequestMessage message = new(HttpMethod.Put, upload);
                message.Content = new ReadOnlyMemoryContent(content);
                message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                return message;
            }, retry: false, cancellationToken);
            RequireSuccess(result);
            Resource? saved = null;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                saved = await MetadataAsync(path, cancellationToken);
                if (saved != null) break;
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)), cancellationToken);
            }
            if (saved == null) throw Error("STORAGE_UPLOAD_PENDING", true, "Загрузка ещё не подтверждена. Повторите попытку позже.");
            Verify(saved, content.Length, hash);
            return new(Key(relative), hash, content.Length);
        }
        finally { transfers.Release(); }
    }

    public async Task<FileStorageHealth> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            Resource? root = await MetadataAsync(options.Root, cancellationToken);
            // A missing application subfolder is valid before the first upload. A successful
            // metadata request (including 404) proves provider reachability and authorization.
            return root == null || root.Type == "dir"
                ? new(true, "STORAGE_READY")
                : new(false, "STORAGE_PATH_CONFLICT");
        }
        catch (FileStorageException exception)
        {
            return new(false, exception.Code);
        }
    }

    public async Task<byte[]> ReadAsync(string storageKey, CancellationToken cancellationToken)
    {
        string relative = RelativeKey(storageKey);
        string path = options.Root + "/" + relative;
        await transfers.WaitAsync(cancellationToken);
        try
        {
            Resource resource = await MetadataAsync(path, cancellationToken)
                ?? throw Error("STORAGE_NOT_FOUND", false, "Файл отсутствует в хранилище.");
            if (resource.Type != "file" || resource.Size is < 1 || resource.Size > options.MaxFileBytes)
                throw Error("STORAGE_INVALID_FILE", false, "Файл в хранилище не соответствует ограничениям.");
            string expectedHash = Path.GetFileNameWithoutExtension(relative).Split('_')[1];
            Verify(resource, resource.Size, expectedHash);
            using JsonDocument link = await ApiJsonAsync(HttpMethod.Get, "/download", path, "", cancellationToken);
            Uri download = TransferUri(link, "GET");
            using HttpResponseMessage response = await DownloadAsync(download, cancellationToken);
            RequireSuccess(response);
            byte[] bytes = await ReadBoundedAsync(response, options.MaxFileBytes, cancellationToken);
            if (bytes.LongLength != resource.Size || Convert.ToHexStringLower(SHA256.HashData(bytes)) != expectedHash)
                throw Error("STORAGE_INTEGRITY", false, "Проверка целостности файла не пройдена.");
            return bytes;
        }
        finally { transfers.Release(); }
    }

    private async Task<HttpResponseMessage> DownloadAsync(Uri uri, CancellationToken cancellationToken)
    {
        // Signed downloads may redirect to another Yandex storage host. Revalidate each hop;
        // never forward the OAuth header or accept a redirect to a local/third-party server.
        for (int hop = 0; hop < 4; hop++)
        {
            HttpResponseMessage response = await SendAsync(() => new(HttpMethod.Get, uri), true, cancellationToken);
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)) return response;
            Uri? location = response.Headers.Location;
            response.Dispose();
            if (location == null) break;
            uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
            ValidateTransferUri(uri);
        }
        throw Error("STORAGE_LINK_INVALID", false, "Хранилище вернуло недопустимую ссылку.");
    }

    private async Task EnsureDirectoryAsync(string directory, CancellationToken cancellationToken)
    {
        string current = "app:";
        foreach (string segment in directory[5..].Split('/'))
        {
            current += "/" + segment;
            using HttpResponseMessage response = await SendAsync(() => ApiRequest(HttpMethod.Put, "", current, ""), true, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                Resource? existing = await MetadataAsync(current, cancellationToken);
                if (existing?.Type != "dir") throw Error("STORAGE_PATH_CONFLICT", false, "Путь хранилища занят файлом.");
            }
            else RequireSuccess(response);
        }
    }

    private async Task<Resource?> MetadataAsync(string path, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendAsync(() => ApiRequest(HttpMethod.Get, "", path, "&fields=type,size,sha256"), true, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        RequireSuccess(response);
        using JsonDocument json = await ReadJsonAsync(response, cancellationToken);
        JsonElement root = json.RootElement;
        string type = String(root, "type");
        return new(type, root.TryGetProperty("size", out JsonElement size) && size.TryGetInt64(out long length) ? length : 0,
            String(root, "sha256"));
    }

    private async Task<JsonDocument> ApiJsonAsync(HttpMethod method, string suffix, string path, string query, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendAsync(() => ApiRequest(method, suffix, path, query), true, cancellationToken);
        RequireSuccess(response);
        return await ReadJsonAsync(response, cancellationToken);
    }

    private HttpRequestMessage ApiRequest(HttpMethod method, string suffix, string path, string query)
    {
        HttpRequestMessage request = new(method, Api + suffix + "?path=" + Uri.EscapeDataString(path) + query);
        request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", options.Token);
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> create, bool retry, CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using HttpRequestMessage request = create();
                HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!retry || attempt >= 2 || (response.StatusCode != HttpStatusCode.TooManyRequests && (int)response.StatusCode < 500)) return response;
                TimeSpan delay = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow)
                    ?? TimeSpan.FromMilliseconds(300 * (attempt + 1));
                response.Dispose();
                if (delay > TimeSpan.FromSeconds(10)) throw Error("STORAGE_BUSY", true, "Хранилище занято. Повторите попытку позже.");
                await Task.Delay(delay < TimeSpan.Zero ? TimeSpan.Zero : delay, cancellationToken);
            }
            catch (HttpRequestException)
            {
                // Do not retain provider exception text/inner exceptions: they may contain signed URLs.
                if (!retry || attempt >= 2) throw Error("STORAGE_UNAVAILABLE", true, "Хранилище временно недоступно.");
                await Task.Delay(TimeSpan.FromMilliseconds(300 * (attempt + 1)), cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { throw Error("STORAGE_TIMEOUT", true, "Хранилище не ответило вовремя."); }
        }
    }

    private static void RequireSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        throw response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => Error("STORAGE_AUTH", false, "Проверьте подключение и права Яндекс Диска."),
            HttpStatusCode.TooManyRequests => Error("STORAGE_BUSY", true, "Хранилище занято. Повторите попытку позже."),
            HttpStatusCode.InsufficientStorage or HttpStatusCode.Locked => Error("STORAGE_QUOTA", false, "Достигнут лимит хранилища или загрузок."),
            HttpStatusCode.NotFound => Error("STORAGE_NOT_FOUND", false, "Файл отсутствует в хранилище."),
            HttpStatusCode.Conflict => Error("STORAGE_CONFLICT", true, "Операция с файлом уже выполняется. Повторите попытку."),
            _ => Error("STORAGE_UNAVAILABLE", (int)response.StatusCode >= 500, "Операция с хранилищем не выполнена.")
        };
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, int maximum, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > maximum) throw Error("STORAGE_SIZE", false, "Превышен допустимый размер ответа хранилища.");
        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(90));
            await using Stream source = await response.Content.ReadAsStreamAsync(timeout.Token);
            using MemoryStream output = new();
            byte[] buffer = new byte[81920];
            int read;
            while ((read = await source.ReadAsync(buffer, timeout.Token)) != 0)
            {
                if (output.Length + read > maximum) throw Error("STORAGE_SIZE", false, "Превышен допустимый размер ответа хранилища.");
                output.Write(buffer, 0, read);
            }
            return output.ToArray();
        }
        catch (HttpRequestException) { throw Error("STORAGE_UNAVAILABLE", true, "Не удалось прочитать файл из хранилища."); }
        catch (IOException exception) when (exception is not FileStorageException) { throw Error("STORAGE_UNAVAILABLE", true, "Не удалось прочитать файл из хранилища."); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw Error("STORAGE_TIMEOUT", true, "Хранилище не ответило вовремя."); }
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        byte[] bytes = await ReadBoundedAsync(response, 64 * 1024, cancellationToken);
        try
        {
            JsonDocument document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind == JsonValueKind.Object) return document;
            document.Dispose();
        }
        catch (JsonException) { }
        throw Error("STORAGE_RESPONSE_INVALID", false, "Хранилище вернуло некорректный ответ.");
    }

    private static string String(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";

    private static Uri TransferUri(JsonDocument json, string method)
    {
        if (String(json.RootElement, "method") != method || !Uri.TryCreate(String(json.RootElement, "href"), UriKind.Absolute, out Uri? uri))
            throw Error("STORAGE_LINK_INVALID", false, "Хранилище вернуло недопустимую ссылку.");
        ValidateTransferUri(uri);
        return uri;
    }

    private static void ValidateTransferUri(Uri uri)
    {
        if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0
            || !(uri.Host.EndsWith(".yandex.net", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".yandex.ru", StringComparison.OrdinalIgnoreCase)))
            throw Error("STORAGE_LINK_INVALID", false, "Хранилище вернуло недопустимую ссылку.");
    }

    private string Key(string relative) => $"yd1:{options.ConnectionId}:{relative}";

    private string RelativeKey(string key)
    {
        string prefix = $"yd1:{options.ConnectionId}:";
        if (!key.StartsWith(prefix, StringComparison.Ordinal)) throw Error("STORAGE_CONNECTION_MISMATCH", false, "Файл относится к другому подключению хранилища.");
        string relative = key[prefix.Length..];
        string[] segments = relative.Split('/');
        string file = segments[^1];
        string name = Path.GetFileNameWithoutExtension(file);
        string[] parts = name.Split('_');
        bool layout = segments.Length == 5 && segments[0] == "Objects"
            && Guid.TryParseExact(segments[1], "N", out _) && Guid.TryParseExact(segments[2], "N", out _)
            && segments[3] is "Photos" or "Documents" or "Audio" or "Video" or "Exports"
            || segments.Length == 3 && segments[0] == "Checks" && Guid.TryParseExact(segments[1], "N", out _);
        if (!layout || parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out _) || parts[1].Length != 64
            || !parts[1].All(char.IsAsciiHexDigit) || !segments[..^1].All(YandexDiskOptions.ValidSegment)
            || file.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '.'))
            || Path.GetExtension(file) is not (".jpg" or ".png" or ".gif" or ".webp" or ".pdf" or ".txt" or ".csv" or ".docx" or ".xlsx" or ".mp4" or ".mp3" or ".bin"))
            throw Error("STORAGE_KEY_INVALID", false, "Некорректный ключ файлового хранилища.");
        return relative;
    }

    private static void Verify(Resource resource, long length, string hash)
    {
        if (resource.Type != "file" || resource.Size != length || !string.Equals(resource.Sha256, hash, StringComparison.OrdinalIgnoreCase))
            throw Error("STORAGE_INTEGRITY", false, "Проверка целостности файла не пройдена.");
    }

    private static string Extension(string contentType) => contentType.ToLowerInvariant() switch
    {
        "image/jpeg" => ".jpg", "image/png" => ".png", "image/gif" => ".gif", "image/webp" => ".webp",
        "application/pdf" => ".pdf", "text/plain" => ".txt", "text/csv" => ".csv",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ".xlsx",
        "video/mp4" => ".mp4", "audio/mpeg" => ".mp3", _ => ".bin"
    };

    private static FileStorageException Error(string code, bool retryable, string message) => new(code, retryable, message);
    private sealed record Resource(string Type, long Size, string Sha256);

    public void Dispose()
    {
        transfers.Dispose();
        if (ownsClient) http.Dispose();
    }
}
