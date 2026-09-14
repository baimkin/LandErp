using LandErp.Application.Foundation;
using LandErp.Infrastructure.Persistence;
using LandErp.Server.Foundation;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.None);
builder.Services.AddLandErpPersistence(builder.Configuration);
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Extensions.TryAdd("code", context.ProblemDetails.Status switch
    {
        404 => "NOT_FOUND",
        401 => "AUTHENTICATION_REQUIRED",
        403 => "FORBIDDEN",
        _ => "REQUEST_FAILED"
    });
    context.ProblemDetails.Extensions["correlationId"] = context.HttpContext.TraceIdentifier;
});

WebApplication app = builder.Build();
app.UseMiddleware<CorrelationMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", async (IDatabaseStatus database, CancellationToken cancellationToken) =>
    await database.IsReadyAsync(cancellationToken) ? Results.Ok(new { status = "ready" })
        : Results.Problem(statusCode: 503, title: "Система временно не готова", extensions:
            new Dictionary<string, object?> { ["code"] = "DB_NOT_READY" }));
app.MapGet("/", () => Results.Ok(new { service = "LandErp", checkpoint = "A" }));
await app.RunAsync();
