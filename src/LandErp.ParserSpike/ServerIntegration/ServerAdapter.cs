using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LandErp.Collector.Contracts.V1;

namespace LandErp.ParserSpike.ServerIntegration;

public sealed record ServerConnection(Uri Origin, Guid AgentId, string Token)
{
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
                // Untrusted response bodies and header values never enter diagnostics.
                string code = response.StatusCode switch { HttpStatusCode.Unauthorized => "AGENT_UNAUTHORIZED",
                    HttpStatusCode.Forbidden => "WORK_NOT_ALLOWED", HttpStatusCode.Conflict => "WORK_OR_IDEMPOTENCY_CONFLICT", HttpStatusCode.BadRequest => "RESULT_INVALID",
                    HttpStatusCode.TooManyRequests => "RETRY_LATER", _ => "SERVER_UNAVAILABLE" };
                throw new ServerDeliveryException(code, (int)response.StatusCode >= 500 || (int)response.StatusCode == 429);
            }
            return await response.Content.ReadFromJsonAsync<T>(CollectionJson.Options, token).ConfigureAwait(false);
        }
        catch (HttpRequestException) { throw new ServerDeliveryException("SERVER_UNAVAILABLE", true); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new ServerDeliveryException("SERVER_TIMEOUT", true); }
    }
}
