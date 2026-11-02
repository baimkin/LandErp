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
        var group = app.MapGroup("/api/procurement").RequireAuthorization();
        group.MapGet("/queue", async (HttpContext http, IProcurementWorkspace workspace, CancellationToken token) => Results.Ok(await workspace.ReadQueueAsync(PermissionAuthorization.SubjectFrom(http.User), new(), token)));
        group.MapGet("/cases/{id:guid}", async (Guid id, HttpContext http, IProcurementWorkspace workspace, CancellationToken token) => Results.Ok(await workspace.ReadCardAsync(PermissionAuthorization.SubjectFrom(http.User), id, token)));
        group.MapPost("/cases/manual", async (CreateManualPropertyCase command, HttpContext http, IProcurementWorkspace workspace, CancellationToken token) =>
            Results.Ok(await workspace.CreateManualCaseAsync(PermissionAuthorization.SubjectFrom(http.User), command, http.TraceIdentifier, token))).AddEndpointFilter(ValidateCsrfAsync);
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
        group.MapPost("/check-templates", async (SaveCheckTemplate command, HttpContext http, IProcurementWorkspace workspace, CancellationToken token) => { await workspace.SaveCheckTemplateAsync(PermissionAuthorization.SubjectFrom(http.User), command, http.TraceIdentifier, token); return Results.NoContent(); }).AddEndpointFilter(ValidateCsrfAsync);
        group.MapPost("/inspection-templates", async (SaveInspectionTemplate command, HttpContext http, IProcurementWorkspace workspace, CancellationToken token) => { await workspace.SaveInspectionTemplateAsync(PermissionAuthorization.SubjectFrom(http.User), command, http.TraceIdentifier, token); return Results.NoContent(); }).AddEndpointFilter(ValidateCsrfAsync);
        group.MapPost("/inspections", async (SaveInspection command, HttpContext http, IProcurementWorkspace workspace, CancellationToken token) => Results.Ok(new { id = await workspace.SaveInspectionAsync(PermissionAuthorization.SubjectFrom(http.User), command, http.TraceIdentifier, token) })).AddEndpointFilter(ValidateCsrfAsync);
        group.MapPost("/acquire", async (MarkCaseAcquired command, HttpContext http, IProcurementWorkspace workspace, CancellationToken token) => { await workspace.MarkAcquiredAsync(PermissionAuthorization.SubjectFrom(http.User), command, http.TraceIdentifier, token); return Results.NoContent(); }).AddEndpointFilter(ValidateCsrfAsync);
        group.MapPost("/acquisition/correct", async (CorrectCaseAcquisition command, HttpContext http, IProcurementWorkspace workspace, CancellationToken token) => { await workspace.CorrectAcquisitionAsync(PermissionAuthorization.SubjectFrom(http.User), command, http.TraceIdentifier, token); return Results.NoContent(); }).AddEndpointFilter(ValidateCsrfAsync);
        group.MapPost("/attachments", async (AddCaseAttachment command, HttpContext http, IProcurementWorkspace workspace, CancellationToken token) =>
            Results.Ok(new { id = await workspace.AddAttachmentAsync(PermissionAuthorization.SubjectFrom(http.User), command, http.TraceIdentifier, token) })).AddEndpointFilter(ValidateCsrfAsync);
        group.MapPost("/documents/requirements", async (SaveDocumentRequirement command, HttpContext http, IProcurementWorkspace workspace, CancellationToken token) => { await workspace.SaveDocumentRequirementAsync(PermissionAuthorization.SubjectFrom(http.User), command, http.TraceIdentifier, token); return Results.NoContent(); }).AddEndpointFilter(ValidateCsrfAsync);
        group.MapPost("/attachments/retry", async (RetryCaseAttachment command, HttpContext http, IProcurementWorkspace workspace, CancellationToken token) => { await workspace.RetryAttachmentAsync(PermissionAuthorization.SubjectFrom(http.User), command, http.TraceIdentifier, token); return Results.NoContent(); }).AddEndpointFilter(ValidateCsrfAsync);
        group.MapPost("/attachments/upload/begin", async (HttpContext http, IProcurementWorkspace workspace, CancellationToken token) =>
            Results.Ok(await workspace.BeginAttachmentUploadAsync(PermissionAuthorization.SubjectFrom(http.User), await UploadMetadataAsync<BeginCaseAttachmentUpload>(http, token), http.TraceIdentifier, token))).AddEndpointFilter(ValidateCsrfAsync).AddEndpointFilter(ValidateUploadBodyAsync);
        group.MapPost("/attachments/upload/retry", async (HttpContext http, IProcurementWorkspace workspace, CancellationToken token) =>
            Results.Ok(await workspace.BeginAttachmentRetryAsync(PermissionAuthorization.SubjectFrom(http.User), await UploadMetadataAsync<BeginCaseAttachmentRetry>(http, token), http.TraceIdentifier, token))).AddEndpointFilter(ValidateCsrfAsync).AddEndpointFilter(ValidateUploadBodyAsync);
        group.MapPost("/attachments/upload/complete", async (HttpContext http, IProcurementWorkspace workspace, CancellationToken token) =>
        { await workspace.CompleteAttachmentUploadAsync(PermissionAuthorization.SubjectFrom(http.User), await UploadMetadataAsync<CompleteCaseAttachmentUpload>(http, token), http.TraceIdentifier, token); return Results.NoContent(); }).AddEndpointFilter(ValidateCsrfAsync).AddEndpointFilter(ValidateUploadBodyAsync);
        group.MapPost("/attachments/upload/cancel", async (HttpContext http, IProcurementWorkspace workspace, CancellationToken token) =>
        { await workspace.CancelAttachmentUploadAsync(PermissionAuthorization.SubjectFrom(http.User), await UploadMetadataAsync<CompleteCaseAttachmentUpload>(http, token), http.TraceIdentifier, token); return Results.NoContent(); }).AddEndpointFilter(ValidateCsrfAsync).AddEndpointFilter(ValidateUploadBodyAsync);
        group.MapPost("/attachments/delete", async (DeleteCaseAttachment command, HttpContext http, IProcurementWorkspace workspace, CancellationToken token) => { await workspace.DeleteAttachmentAsync(PermissionAuthorization.SubjectFrom(http.User), command, http.TraceIdentifier, token); return Results.NoContent(); }).AddEndpointFilter(ValidateCsrfAsync);
        group.MapGet("/attachments/{id:guid}/image", async (Guid id, HttpContext http, IProcurementWorkspace workspace, CancellationToken token) =>
        {
            // Reuse the same visibility and integrity boundary as downloads; never redirect inline images.
            AttachmentContent attachment = await workspace.ReadAttachmentAsync(PermissionAuthorization.SubjectFrom(http.User), id, token);
            if (attachment.ExternalUrl != null || !CaseNoteDocument.IsImageType(attachment.ContentType)) return Results.NotFound();
            http.Response.Headers.XContentTypeOptions = "nosniff";
            http.Response.Headers.CacheControl = "no-store";
            return Results.File(attachment.Content!, attachment.ContentType);
        });
        group.MapGet("/attachments/{id:guid}", async (Guid id, HttpContext http, IProcurementWorkspace workspace, CancellationToken token) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers.XContentTypeOptions = "nosniff";
            AttachmentContent attachment = await workspace.ReadAttachmentAsync(PermissionAuthorization.SubjectFrom(http.User), id, token);
            return attachment.ExternalUrl != null ? Results.Redirect(attachment.ExternalUrl) : Results.File(attachment.Content!, attachment.ContentType, attachment.OriginalName);
        });
        group.MapPost("/facts/source", async (ApplySourceFact command, HttpContext http, IProcurementWorkspace workspace, CancellationToken token) => { await workspace.ApplySourceFactAsync(PermissionAuthorization.SubjectFrom(http.User), command, http.TraceIdentifier, token); return Results.NoContent(); }).AddEndpointFilter(ValidateCsrfAsync);

        app.MapGet("/api/inspection/attachments/{id:guid}", async (Guid id, HttpContext http, IProcurementWorkspace workspace, CancellationToken token) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers.XContentTypeOptions = "nosniff";
            AttachmentContent attachment = await workspace.ReadAttachmentAsync(PermissionAuthorization.SubjectFrom(http.User), id, token);
            return attachment.ExternalUrl != null ? Results.Redirect(attachment.ExternalUrl) : Results.File(attachment.Content!, attachment.ContentType, attachment.OriginalName);
        }).RequireAuthorization();
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
        group.MapGet("/incoming/procurement-targets", async (HttpContext http, ICatalogWorkspace workspace, CancellationToken token) =>
            Results.Ok(await workspace.ReadProcurementTargetsAsync(PermissionAuthorization.SubjectFrom(http.User), token)));
        group.MapPost("/incoming/take-to-work", async (TakeCatalogItemToWork command, HttpContext http, ICatalogWorkspace workspace, CancellationToken token) =>
            Results.Ok(await workspace.TakeToWorkAsync(PermissionAuthorization.SubjectFrom(http.User), command, http.TraceIdentifier, token))).AddEndpointFilter(ValidateCsrfAsync);
        group.MapPost("/incoming/transfer-to-procurement", async (TransferCatalogItemToProcurement command, HttpContext http, ICatalogWorkspace workspace, CancellationToken token) =>
            Results.Ok(await workspace.TransferToProcurementAsync(PermissionAuthorization.SubjectFrom(http.User), command, http.TraceIdentifier, token))).AddEndpointFilter(ValidateCsrfAsync);
        group.MapPost("/incoming/resume-case", async (ResumeCatalogItemCase command, HttpContext http, ICatalogWorkspace workspace, CancellationToken token) =>
            Results.Ok(await workspace.ResumeCaseAsync(PermissionAuthorization.SubjectFrom(http.User), command, http.TraceIdentifier, token))).AddEndpointFilter(ValidateCsrfAsync);
    }
    private static async ValueTask<object?> ValidateCsrfAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try { await context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context.HttpContext); }
        catch (AntiforgeryValidationException) { return Results.Problem(statusCode: 400, title: "Проверка запроса не пройдена", extensions: new Dictionary<string, object?> { ["code"] = "CSRF_VALIDATION_FAILED" }); }
        return await next(context);
    }
    private static async ValueTask<object?> ValidateUploadBodyAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try { return await next(context); }
        catch (BadHttpRequestException error) when (error.StatusCode is 413 or 415)
        { return Results.Problem(statusCode: error.StatusCode, title: "Некорректные метаданные загрузки", extensions: new Dictionary<string, object?> { ["code"] = "UPLOAD_METADATA_INVALID" }); }
    }
    // Capability responses must never be cached. These endpoints accept metadata only, even for chunked requests.
    private static async Task<T> UploadMetadataAsync<T>(HttpContext http, CancellationToken token)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!http.Request.HasJsonContentType()) throw new BadHttpRequestException("Ожидаются JSON-метаданные.", StatusCodes.Status415UnsupportedMediaType);
        const int limit = 64 * 1024;
        if (http.Request.ContentLength > limit) throw new BadHttpRequestException("Метаданные слишком велики.", StatusCodes.Status413PayloadTooLarge);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[8192];
        int read;
        while ((read = await http.Request.Body.ReadAsync(chunk, token)) > 0)
        {
            if (buffer.Length + read > limit) throw new BadHttpRequestException("Метаданные слишком велики.", StatusCodes.Status413PayloadTooLarge);
            buffer.Write(chunk, 0, read);
        }
        try { return System.Text.Json.JsonSerializer.Deserialize<T>(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), http.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions) ?? throw new ArgumentException("Некорректные метаданные."); }
        catch (System.Text.Json.JsonException) { throw new ArgumentException("Некорректные метаданные."); }
    }
}
