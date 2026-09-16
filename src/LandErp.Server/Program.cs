using LandErp.Application.Foundation;
using LandErp.Infrastructure.Persistence;
using LandErp.Server.Foundation;
using LandErp.Infrastructure.Modules.IdentityAccess;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Organization.Contracts;
using LandErp.Server.Security;
using LandErp.Server.Components;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using LandErp.Infrastructure.Modules.Collection;
using LandErp.Infrastructure.Modules.Procurement;
using System.Text.Json.Serialization;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
if (builder.Environment.IsEnvironment("Local") || builder.Environment.IsEnvironment("Test"))
    builder.WebHost.UseStaticWebAssets();
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.None);
builder.Services.AddLandErpPersistence(builder.Configuration);
builder.Services.AddLandErpIdentity();
builder.Services.AddLandErpCollection();
builder.Services.AddLandErpProcurement(builder.Configuration, builder.Environment);
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 8 * 1024 * 1024);
builder.Services.AddScoped<AccountActivation>();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, RevalidatingIdentityState>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorization>();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/account/login";
    options.AccessDeniedPath = "/account/forbidden";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api")) context.Response.StatusCode = 401;
        else context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api")) context.Response.StatusCode = 403;
        else context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
});
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.Zero;
    options.OnRefreshingPrincipal = context =>
    {
        // Retain the proof of this signed MFA login only after the security stamp was validated.
        if (context.NewPrincipal?.Identities.FirstOrDefault() is { } identity && context.CurrentPrincipal is { } current)
            identity.AddClaims(current.FindAll("amr"));
        return Task.CompletedTask;
    };
});
builder.Services.AddAuthorization(options =>
{
    foreach (string permission in new[] { Permissions.UsersRead, Permissions.UsersManage,
        Permissions.OrganizationManage, Permissions.RolesManage, Permissions.AuditRead,
        Permissions.AgentsManage, Permissions.CollectionManage, Permissions.QueueRead,
        Permissions.ManagerDecide, Permissions.HeadDecide })
    {
        options.AddPolicy(permission, policy => policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission)));
    }
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("collector", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("account", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 15, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
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
builder.Services.AddExceptionHandler<SafeExceptionHandler>();

WebApplication app = builder.Build();
app.UseMiddleware<CorrelationMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/account") && !context.Request.IsHttps)
    {
        await Results.Problem(statusCode: 400, title: "Для входа требуется HTTPS").ExecuteAsync(context);
        return;
    }
    await next(context);
});
app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true && !context.Request.Path.StartsWithSegments("/account")
        && !context.Request.Path.StartsWithSegments("/_framework") && !context.Request.Path.StartsWithSegments("/_content")
        && !Path.HasExtension(context.Request.Path))
    {
        UserManager<LandErpUser> users = context.RequestServices.GetRequiredService<UserManager<LandErpUser>>();
        LandErpUser? user = await users.GetUserAsync(context.User);
        LandErpDbContext db = context.RequestServices.GetRequiredService<LandErpDbContext>();
        bool active = user != null && await db.Employees.AnyAsync(item => item.UserId == user.Id && item.Active, context.RequestAborted);
        if (!active)
        {
            await context.SignOutAsync(IdentityConstants.ApplicationScheme);
            if (context.Request.Path.StartsWithSegments("/api")) context.Response.StatusCode = 401;
            else context.Response.Redirect("/account/login");
            return;
        }
        if (user!.MustChangePassword)
        {
            if (context.Request.Path.StartsWithSegments("/api")) context.Response.StatusCode = 403;
            else context.Response.Redirect("/account/change-password");
            return;
        }
    }
    await next(context);
});
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapCollectorEndpoints();
app.MapProcurementEndpoints();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", async (IDatabaseStatus database, CancellationToken cancellationToken) =>
    await database.IsReadyAsync(cancellationToken) ? Results.Ok(new { status = "ready" })
        : Results.Problem(statusCode: 503, title: "Система временно не готова", extensions:
            new Dictionary<string, object?> { ["code"] = "DB_NOT_READY" }));
app.MapGet("/api/organization", async (HttpContext context, IOrganizationWorkspace workspace, CancellationToken cancellationToken) =>
    Results.Ok(await workspace.ReadAsync(PermissionAuthorization.SubjectFrom(context.User), cancellationToken)))
    .RequireAuthorization(Permissions.UsersRead);
app.MapGet("/api/audit", async (HttpContext context, IOrganizationWorkspace workspace, CancellationToken cancellationToken) =>
    Results.Ok(await workspace.ReadAuditAsync(PermissionAuthorization.SubjectFrom(context.User), cancellationToken)))
    .RequireAuthorization(Permissions.AuditRead);
await app.RunAsync();
