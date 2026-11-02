using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using LandErp.Application.Foundation.Files;
using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Infrastructure.Modules.Procurement;
using LandErp.Server.Foundation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
public sealed class DirectUploadEndpointTests
{
    [TestMethod, TestCategory("PostgreSQL")]
    public async Task ActualMetadataEndpointsRequireCookieCsrfScopeAndBoundEvenChunkedBodies()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        Guid id = await f.InsertIndependentCaseAsync("API direct");
        var storage = new DirectAttachmentUploadTests.DirectStorage(f.FileStorage);
        var workspace = new ProcurementWorkspace(f.Factory, TimeProvider.System, storage);
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<IProcurementWorkspace>(workspace);
        builder.Services.AddSingleton<ICatalogWorkspace>(workspace);
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddAuthentication("test").AddCookie("test", options =>
        {
            options.Events = new CookieAuthenticationEvents { OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; } };
        });
        builder.Services.AddAuthorization(); builder.Services.AddAntiforgery(o => o.HeaderName = "X-CSRF-TOKEN");
        builder.Services.AddProblemDetails(); builder.Services.AddExceptionHandler<SafeExceptionHandler>();
        await using var app = builder.Build();
        app.UseExceptionHandler(); app.UseAuthentication(); app.UseAuthorization();
        // Test-only sign-in on an ephemeral loopback host. No production auth hooks.
        app.MapGet("/test-login/{foreign:bool}", async (bool foreign, HttpContext http) =>
        {
            var subject = foreign ? f.ForeignOwner : f.Manager;
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, subject.UserId.ToString()), new Claim("amr", subject.MultiFactorAuthenticated ? "mfa" : "pwd")], "test");
            await http.SignInAsync("test", new ClaimsPrincipal(identity));
        });
        typeof(SafeExceptionHandler).Assembly.GetType("LandErp.Server.Foundation.ProcurementEndpoints")!
            .GetMethod("MapProcurementEndpoints", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [app]);
        await app.StartAsync();
        using var handler = new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(app.Urls.Single()) };
        const string route = "/api/procurement/attachments/upload/begin";
        var command = new BeginCaseAttachmentUpload(new(id, CaseAttachmentOwner.Case, null, CaseAttachmentKind.Video, "API video", "", "large.mov", "video/quicktime", null, null, UploadId: Guid.CreateVersion7()), FileUploadLimits.MaxRawFileBytes, new string('a', 64));
        using (var response = await client.PostAsJsonAsync(route, command)) Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        await client.GetAsync("/test-login/false");
        using (var response = await client.PostAsJsonAsync(route, command)) Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/procurement/antiforgery");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("requestToken").GetString());
        CaseAttachmentUploadTicket ticket;
        using (var response = await client.PostAsJsonAsync(route, command))
        {
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
            Assert.IsTrue(response.Headers.CacheControl!.NoStore);
            ticket = (await response.Content.ReadFromJsonAsync<CaseAttachmentUploadTicket>())!;
        }
        Assert.AreEqual(1, storage.Prepares);
        using (var response = await client.PostAsJsonAsync(route, command with { SizeBytes = (long)FileUploadLimits.MaxRawFileBytes + 1 })) Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        using (var response = await client.PostAsync(route, new StringContent(new string(' ', 65537), Encoding.UTF8, "application/json"))) Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        using (var response = await client.PostAsync(route, new ChunkedMetadata())) Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        using (var response = await client.PostAsync(route, new StringContent("{}", Encoding.UTF8, "text/plain"))) Assert.AreEqual(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.AreEqual(1, storage.Prepares);
        storage.Uploaded = true;
        using (var response = await client.PostAsJsonAsync("/api/procurement/attachments/upload/complete", new CompleteCaseAttachmentUpload(id, ticket.AttachmentId))) Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
        Assert.AreEqual(0, storage.BufferedWrites);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        await client.GetAsync("/test-login/true");
        csrf = await client.GetFromJsonAsync<JsonElement>("/api/procurement/antiforgery");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("requestToken").GetString());
        using (var response = await client.PostAsJsonAsync("/api/procurement/attachments/upload/complete", new CompleteCaseAttachmentUpload(id, ticket.AttachmentId))) Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        await app.StopAsync();
    }
    private sealed class ChunkedMetadata : HttpContent
    {
        public ChunkedMetadata() => Headers.ContentType = new("application/json");
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(new byte[65537]).AsTask();
    }
}