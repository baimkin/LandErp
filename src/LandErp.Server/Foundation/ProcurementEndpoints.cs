using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Catalog.Domain;
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
        group.MapGet("/cases/{id:guid}", async (Guid id, HttpContext http, IProcurementWorkspace workspace, CancellationToken token) => Results.Ok(await workspace.ReadCardAsync(PermissionAuthorization.SubjectFrom(http.User), id, token)));
        group.MapGet("/listings/{id:guid}", async (Guid id, HttpContext http, IProcurementWorkspace workspace, CancellationToken token) =>
        {
            Guid? caseId = await workspace.ResolveLegacyListingAsync(PermissionAuthorization.SubjectFrom(http.User), id, token);
            return caseId == null ? Results.NotFound() : Results.Redirect($"/api/procurement/cases/{caseId}", permanent: false);
        });
        group.MapGet("/antiforgery", (HttpContext http, IAntiforgery antiforgery) => { http.Response.Headers.CacheControl = "no-store"; return Results.Ok(new { requestToken = antiforgery.GetAndStoreTokens(http).RequestToken }); });
        group.MapPost("/decisions", async (DecisionCommand command, HttpContext http, IProcurementWorkspace workspace, CancellationToken token) => { await workspace.DecideAsync(PermissionAuthorization.SubjectFrom(http.User), command, http.TraceIdentifier, token); return Results.NoContent(); }).AddEndpointFilter(ValidateCsrfAsync);
        group.MapPost("/notes", async (AddCaseNote command, HttpContext http, IProcurementWorkspace workspace, CancellationToken token) => { await workspace.AddNoteAsync(PermissionAuthorization.SubjectFrom(http.User), command, http.TraceIdentifier, token); return Results.NoContent(); }).AddEndpointFilter(ValidateCsrfAsync);
        group.MapPost("/negotiations", async (AddNegotiation command, HttpContext http, IProcurementWorkspace workspace, CancellationToken token) => { await workspace.AddNegotiationAsync(PermissionAuthorization.SubjectFrom(http.User), command, http.TraceIdentifier, token); return Results.NoContent(); }).AddEndpointFilter(ValidateCsrfAsync);
        group.MapPost("/checks", async (SaveCaseCheck command, HttpContext http, IProcurementWorkspace workspace, CancellationToken token) => { await workspace.SaveCheckAsync(PermissionAuthorization.SubjectFrom(http.User), command, http.TraceIdentifier, token); return Results.NoContent(); }).AddEndpointFilter(ValidateCsrfAsync);
        group.MapPost("/attachments", async (AddCaseAttachment command, HttpContext http, IProcurementWorkspace workspace, CancellationToken token) =>
            Results.Ok(new { id = await workspace.AddAttachmentAsync(PermissionAuthorization.SubjectFrom(http.User), command, http.TraceIdentifier, token) })).AddEndpointFilter(ValidateCsrfAsync);
        group.MapGet("/attachments/{id:guid}", async (Guid id, HttpContext http, IProcurementWorkspace workspace, CancellationToken token) =>
        {
            AttachmentContent attachment = await workspace.ReadAttachmentAsync(PermissionAuthorization.SubjectFrom(http.User), id, token);
            return attachment.ExternalUrl != null ? Results.Redirect(attachment.ExternalUrl) : Results.File(attachment.Content!, attachment.ContentType, attachment.OriginalName);
        });
        group.MapPost("/facts/source", async (ApplySourceFact command, HttpContext http, IProcurementWorkspace workspace, CancellationToken token) => { await workspace.ApplySourceFactAsync(PermissionAuthorization.SubjectFrom(http.User), command, http.TraceIdentifier, token); return Results.NoContent(); }).AddEndpointFilter(ValidateCsrfAsync);
        group.MapGet("/incoming", async (string? text, CatalogSource? source, CatalogDisposition? disposition,
            CatalogAgeRange? age, decimal? minPrice, decimal? maxPrice, decimal? minAreaSquareMeters,
            decimal? maxAreaSquareMeters, bool? attentionOnly, int? offset, int? size, HttpContext http,
            ICatalogWorkspace workspace, CancellationToken token) => Results.Ok(await workspace.ReadIncomingAsync(
                PermissionAuthorization.SubjectFrom(http.User), new(text ?? "", source, disposition,
                    age ?? CatalogAgeRange.Any, minPrice, maxPrice, minAreaSquareMeters, maxAreaSquareMeters,
                    attentionOnly ?? false, offset ?? 0, size ?? 40), token)));
        group.MapGet("/incoming/{id:guid}", async (Guid id, HttpContext http, ICatalogWorkspace workspace, CancellationToken token) =>
            Results.Ok(await workspace.ReadItemAsync(PermissionAuthorization.SubjectFrom(http.User), id, token)));
        group.MapPost("/incoming/manual", async (CreateManualCatalogItem command, HttpContext http, ICatalogWorkspace workspace, CancellationToken token) =>
            Results.Ok(new { id = await workspace.CreateManualAsync(PermissionAuthorization.SubjectFrom(http.User), command, http.TraceIdentifier, token) })).AddEndpointFilter(ValidateCsrfAsync);
        group.MapPost("/incoming/disposition", async (SetCatalogDisposition command, HttpContext http, ICatalogWorkspace workspace, CancellationToken token) =>
        { await workspace.SetDispositionAsync(PermissionAuthorization.SubjectFrom(http.User), command, http.TraceIdentifier, token); return Results.NoContent(); }).AddEndpointFilter(ValidateCsrfAsync);
        group.MapPost("/incoming/monitoring", async (SetCatalogMonitoring command, HttpContext http, ICatalogWorkspace workspace, CancellationToken token) =>
        { await workspace.SetMonitoringAsync(PermissionAuthorization.SubjectFrom(http.User), command, http.TraceIdentifier, token); return Results.NoContent(); }).AddEndpointFilter(ValidateCsrfAsync);
        group.MapPost("/incoming/take-to-work", async (TakeCatalogItemToWork command, HttpContext http, ICatalogWorkspace workspace, CancellationToken token) =>
            Results.Ok(await workspace.TakeToWorkAsync(PermissionAuthorization.SubjectFrom(http.User), command, http.TraceIdentifier, token))).AddEndpointFilter(ValidateCsrfAsync);
        group.MapPost("/incoming/resume-case", async (ResumeCatalogItemCase command, HttpContext http, ICatalogWorkspace workspace, CancellationToken token) =>
            Results.Ok(await workspace.ResumeCaseAsync(PermissionAuthorization.SubjectFrom(http.User), command, http.TraceIdentifier, token))).AddEndpointFilter(ValidateCsrfAsync);
    }
    private static async ValueTask<object?> ValidateCsrfAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try { await context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context.HttpContext); }
        catch (AntiforgeryValidationException) { return Results.Problem(statusCode: 400, title: "Проверка запроса не пройдена", extensions: new Dictionary<string, object?> { ["code"] = "CSRF_VALIDATION_FAILED" }); }
        return await next(context);
    }
}
