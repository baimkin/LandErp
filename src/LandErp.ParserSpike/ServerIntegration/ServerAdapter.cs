using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LandErp.Collector.Contracts.V1;

namespace LandErp.ParserSpike.ServerIntegration;

public sealed record ServerConnection(Uri Origin, Guid AgentId, string Token)
{
    public static ServerConnection FromConnectionCode(string code)
    {
        CollectorConnectionEnvelope envelope = CollectorConnectionCode.Parse(code);
        return new(new Uri(envelope.ServerOrigin), envelope.AgentId, envelope.Token);
    }
    public static ServerConnection FromEnvironment()
    {
        if (!Uri.TryCreate(Environment.GetEnvironmentVariable("LANDERP_COLLECTOR_SERVER_URL"), UriKind.Absolute, out Uri? origin)
            || origin.Scheme != Uri.UriSchemeHttps || origin.UserInfo.Length != 0 || origin.AbsolutePath != "/"
            || origin.Query.Length != 0 || origin.Fragment.Length != 0
            || !Guid.TryParse(Environment.GetEnvironmentVariable("LANDERP_COLLECTOR_AGENT_ID"), out Guid id))
            throw new InvalidOperationException("Настройте LANDERP_COLLECTOR_SERVER_URL и LANDERP_COLLECTOR_AGENT_ID локально вне Git.");
        string token = Environment.GetEnvironmentVariable("LANDERP_COLLECTOR_TOKEN") ?? "";
        if (token.Length != 64 || !token.All(char.IsAsciiHexDigit))
            throw new InvalidOperationException("Настройте LANDERP_COLLECTOR_TOKEN локально. Значение не выводится.");
        return new(origin, id, token);
    }
    public override string ToString() => "Collector server connection (credentials hidden)";
}
public sealed class ServerDeliveryException(string code, bool retryable) : Exception("Server: " + code)
{
    public string Code { get; } = code;
    public bool Retryable { get; } = retryable;
}

/// <summary>HTTPS transport only. Default certificate verification is never disabled in runtime.</summary>
public sealed class ServerAdapter(HttpClient http, ServerConnection connection)
{
    public async Task RegisterAsync(CancellationToken token) => await SendAsync<object>("registration", new AgentRegistration(1, "stage1.1", [ListingSource.Avito, ListingSource.Cian]), token);
    public async Task HeartbeatAsync(AgentHeartbeat heartbeat, CancellationToken token) => await SendAsync<object>("heartbeat", heartbeat, token);
    public Task<CollectionWork?> ClaimAsync(CancellationToken token) => SendAsync<CollectionWork>("work/claim", null, token);
    public async Task<CollectorWorkspace> ReadWorkspaceAsync(CancellationToken token) =>
        await SendAsync<CollectorWorkspace>("workspace", null, token).ConfigureAwait(false)
        ?? throw new ServerDeliveryException("INVALID_WORKSPACE", false);
    public async Task<CollectorGroupView> CreateGroupAsync(CreateCollectorGroup command, CancellationToken token) =>
        await SendAsync<CollectorGroupView>("workspace/groups", command, token).ConfigureAwait(false)
        ?? throw new ServerDeliveryException("INVALID_GROUP", false);
    public async Task<CollectorSearchView> CreateSearchAsync(CreateCollectorSearch command, CancellationToken token) =>
        await SendAsync<CollectorSearchView>("workspace/searches", command, token).ConfigureAwait(false)
        ?? throw new ServerDeliveryException("INVALID_SEARCH", false);
    public async Task<CollectionReceipt> SendResultAsync(CollectionResult result, CancellationToken token) =>
        await SendAsync<CollectionReceipt>("results", result, token) ?? throw new ServerDeliveryException("INVALID_RECEIPT", false);
    private async Task<T?> SendAsync<T>(string path, object? body, CancellationToken token)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, new Uri(connection.Origin, "api/collector/v1/" + path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.Token);
        request.Headers.Add("X-LandErp-Agent-Id", connection.AgentId.ToString());
        request.Headers.Add("X-Correlation-ID", Guid.CreateVersion7().ToString());
        if (body != null) request.Content = JsonContent.Create(body, options: CollectionJson.Options);
        try
        {
            using HttpResponseMessage response = await http.SendAsync(request, token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NoContent) return default;
            if (!response.IsSuccessStatusCode)
            {
                // Only a bounded machine code is read. Arbitrary response text never enters diagnostics.
                string code = await ProblemCodeAsync(response, token).ConfigureAwait(false) ?? response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => CollectorErrorCodes.AgentUnauthorized,
                    HttpStatusCode.Forbidden => CollectorErrorCodes.WorkNotAllowed,
                    HttpStatusCode.Conflict => "CONFLICT",
                    HttpStatusCode.BadRequest => "RESULT_INVALID",
                    HttpStatusCode.TooManyRequests => "RETRY_LATER",
                    _ => "SERVER_UNAVAILABLE"
                };
                bool retryable = (int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests;
                throw new ServerDeliveryException(code, retryable);
            }
            return await response.Content.ReadFromJsonAsync<T>(CollectionJson.Options, token).ConfigureAwait(false);
        }
        catch (HttpRequestException) { throw new ServerDeliveryException("SERVER_UNAVAILABLE", true); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new ServerDeliveryException("SERVER_TIMEOUT", true); }
    }
    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response, CancellationToken token)
    {
        if (response.Content.Headers.ContentLength is > 16384) return null;
        string? mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType != null && !mediaType.Contains("json", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            await response.Content.LoadIntoBufferAsync(16384, token).ConfigureAwait(false);
            using Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: token).ConfigureAwait(false);
            if (!document.RootElement.TryGetProperty("code", out JsonElement value) || value.ValueKind != JsonValueKind.String) return null;
            string? code = value.GetString();
            return code is { Length: > 0 and <= 64 } && code.All(character => char.IsAsciiLetterUpper(character) || char.IsAsciiDigit(character) || character == '_')
                ? code : null;
        }
        catch (Exception exception) when (exception is JsonException or HttpRequestException or InvalidOperationException) { return null; }
    }
}
