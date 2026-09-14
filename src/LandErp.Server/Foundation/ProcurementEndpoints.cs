using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Server.Security;
using Microsoft.AspNetCore.Antiforgery;

namespace LandErp.Server.Foundation;

internal static class ProcurementEndpoints
{
    public static void MapProcurementEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/procurement").RequireAuthorization(Permissions.QueueRead);
        group.MapGet("/queue", async (HttpContext http, IProcurementWorkspace workspace, CancellationToken token) => Results.Ok(await workspace.ReadQueueAsync(PermissionAuthorization.SubjectFrom(http.User), new(), token)));
        group.MapGet("/listings/{id:guid}", async (Guid id, HttpContext http, IProcurementWorkspace workspace, CancellationToken token) => Results.Ok(await workspace.ReadCardAsync(PermissionAuthorization.SubjectFrom(http.User), id, token)));
        group.MapGet("/antiforgery", (HttpContext http, IAntiforgery antiforgery) => { http.Response.Headers.CacheControl = "no-store"; return Results.Ok(new { requestToken = antiforgery.GetAndStoreTokens(http).RequestToken }); });
        group.MapPost("/decisions", async (DecisionCommand command, HttpContext http, IProcurementWorkspace workspace, CancellationToken token) => { await workspace.DecideAsync(PermissionAuthorization.SubjectFrom(http.User), command, http.TraceIdentifier, token); return Results.NoContent(); }).AddEndpointFilter(ValidateCsrfAsync);
        group.MapPost("/notes", async (AddCaseNote command, HttpContext http, IProcurementWorkspace workspace, CancellationToken token) => { await workspace.AddNoteAsync(PermissionAuthorization.SubjectFrom(http.User), command, http.TraceIdentifier, token); return Results.NoContent(); }).AddEndpointFilter(ValidateCsrfAsync);
    }
    private static async ValueTask<object?> ValidateCsrfAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try { await context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context.HttpContext); }
        catch (AntiforgeryValidationException) { return Results.Problem(statusCode: 400, title: "Проверка запроса не пройдена", extensions: new Dictionary<string, object?> { ["code"] = "CSRF_VALIDATION_FAILED" }); }
        return await next(context);
    }
}
