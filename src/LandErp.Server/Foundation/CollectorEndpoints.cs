using LandErp.Application.Modules.Collection.Contracts;
using LandErp.Collector.Contracts.V1;
using Microsoft.AspNetCore.RateLimiting;

namespace LandErp.Server.Foundation;

internal static class CollectorEndpoints
{
    public static void MapCollectorEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/collector/v1").RequireRateLimiting("collector");
        group.AddEndpointFilter(async (context, next) =>
        {
            if (!context.HttpContext.Request.IsHttps) return Results.Problem(statusCode: 400, title: "HTTPS required");
            return await next(context);
        });
        group.MapPost("/registration", async (HttpContext http, AgentRegistration request, ICollectorGateway gateway, CancellationToken token) =>
        { await gateway.RegisterAsync(Credentials(http), request, token); return Results.Ok(new { contractVersion = 1 }); });
        group.MapPost("/heartbeat", async (HttpContext http, AgentHeartbeat request, ICollectorGateway gateway, CancellationToken token) =>
        { await gateway.HeartbeatAsync(Credentials(http), request, token); return Results.Ok(new { status = "online" }); });
        group.MapPost("/work/claim", async (HttpContext http, ICollectorGateway gateway, CancellationToken token) =>
        { CollectionWork? work = await gateway.ClaimAsync(Credentials(http), token); return work == null ? Results.NoContent() : Results.Ok(work); });
        group.MapPost("/results", async (HttpContext http, CollectionResult request, ICollectorGateway gateway, CancellationToken token) =>
            Results.Ok(await gateway.AcceptAsync(Credentials(http), request, token)));
    }
    private static AgentCredential Credentials(HttpContext http)
    {
        string authorization = http.Request.Headers.Authorization.ToString();
        if (!Guid.TryParse(http.Request.Headers["X-LandErp-Agent-Id"].ToString(), out Guid id)
            || !authorization.StartsWith("Bearer ", StringComparison.Ordinal) || authorization.Length != 71)
            throw new CollectorProtocolException("AGENT_UNAUTHORIZED");
        return new(id, authorization[7..]);
    }
}
