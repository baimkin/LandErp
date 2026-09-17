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
        group.MapPost("/activation", async (AgentActivation request, ICollectorGateway gateway, CancellationToken token) =>
            Results.Ok(await gateway.ActivateAsync(request, token)));
        group.MapPost("/registration", async (HttpContext http, AgentRegistration request, ICollectorGateway gateway, CancellationToken token) =>
        { await gateway.RegisterAsync(Credentials(http), request, token); return Results.Ok(new { contractVersion = 1 }); });
        group.MapPost("/heartbeat", async (HttpContext http, AgentHeartbeat request, ICollectorGateway gateway, CancellationToken token) =>
        { await gateway.HeartbeatAsync(Credentials(http), request, token); return Results.Ok(new { status = "online" }); });
        group.MapPost("/work/claim", async (HttpContext http, ICollectorGateway gateway, CancellationToken token) =>
        { CollectionWork? work = await gateway.ClaimAsync(Credentials(http), token); return work == null ? Results.NoContent() : Results.Ok(work); });
        group.MapPost("/workspace", async (HttpContext http, ICollectorGateway gateway, CancellationToken token) =>
            Results.Ok(await gateway.ReadWorkspaceAsync(Credentials(http), token)));
        group.MapPost("/workspace/groups/update", async (HttpContext http, UpdateCollectorGroup request, ICollectorGateway gateway, CancellationToken token) =>
            Results.Ok(await gateway.UpdateGroupAsync(Credentials(http), request, token)));
        group.MapPost("/workspace/searches/update", async (HttpContext http, UpdateCollectorSearch request, ICollectorGateway gateway, CancellationToken token) =>
            Results.Ok(await gateway.UpdateSearchAsync(Credentials(http), request, token)));
        group.MapPost("/workspace/searches/run", async (HttpContext http, RunCollectorSearch request, ICollectorGateway gateway, CancellationToken token) =>
        { await gateway.EnqueueSearchAsync(Credentials(http), request, token); return Results.Ok(new { queued = true }); });
        group.MapPost("/workspace/groups", async (HttpContext http, CreateCollectorGroup request, ICollectorGateway gateway, CancellationToken token) =>
            Results.Ok(await gateway.CreateGroupAsync(Credentials(http), request, token)));
        group.MapPost("/workspace/searches", async (HttpContext http, CreateCollectorSearch request, ICollectorGateway gateway, CancellationToken token) =>
            Results.Ok(await gateway.CreateSearchAsync(Credentials(http), request, token)));
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
