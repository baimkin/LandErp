using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace LandErp.StorageProbe;

internal static class ProbeSelfTest
{
    public static async Task<int> RunAsync(Action<string> stage)
    {
        // Real browser, two distinct local origins; no credentials, Yandex or production traffic.
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        string origin = "";
        byte[] payload = Encoding.UTF8.GetBytes("synthetic probe self-test");
        int puts = 0, options = 0;
        bool contentMatches = false;
        app.Run(async context =>
        {
            if (context.Request.Path == "/fixture")
            {
                context.Response.ContentType = "text/html";
                await context.Response.WriteAsync("<!doctype html><title>Probe self-test</title>");
                return;
            }
            bool allow = context.Request.Path == "/allowed";
            if (allow)
            {
                context.Response.Headers.AccessControlAllowOrigin = origin;
                context.Response.Headers.AccessControlAllowMethods = "PUT";
                context.Response.Headers.AccessControlAllowHeaders = "content-type";
            }
            if (context.Request.Method == "OPTIONS")
            {
                Interlocked.Increment(ref options);
                context.Response.StatusCode = 204;
            }
            else if (context.Request.Method == "PUT")
            {
                Interlocked.Increment(ref puts);
                using var buffer = new MemoryStream();
                await context.Request.Body.CopyToAsync(buffer);
                contentMatches = buffer.ToArray().SequenceEqual(payload);
                context.Response.StatusCode = 201;
            }
            else context.Response.StatusCode = 405;
        });
        stage("SelfTestServerStart");
        await app.StartAsync();
        origin = app.Urls.Single();
        string uploadOrigin = new UriBuilder(origin) { Host = "localhost" }.Uri.GetLeftPart(UriPartial.Authority);
        stage("SelfTestDriverStart");
        using var playwright = await Playwright.CreateAsync().WaitAsync(TimeSpan.FromSeconds(30));
        stage("SelfTestEdgeLaunch");
        await using var browser = await playwright.Chromium.LaunchAsync(new()
            { Channel = "msedge", Headless = true, ChromiumSandbox = true, Timeout = 30000 });
        await using var browserContext = await browser.NewContextAsync();
        var page = await browserContext.NewPageAsync();

        stage("SelfTestPage");
        await page.GotoAsync(origin + "/fixture");
        stage("SelfTestAllowedPut");
        var allowed = await BrowserPut.RunAsync(page, new Uri(uploadOrigin + "/allowed"), payload);
        if (allowed.Event != "load" || allowed.Status != 201 || !contentMatches || puts != 1 || !allowed.ProgressObserved)
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { Allowed = allowed, Puts = puts, Options = options, ContentMatches = contentMatches }));
            throw new ProbeFailure("SELF_TEST_ALLOWED_PUT_FAILED");
        }
        stage("SelfTestBlockedPut");
        var blocked = await BrowserPut.RunAsync(page, new Uri(uploadOrigin + "/blocked"), payload);
        if (blocked.Event != "error" || blocked.Status != 0 || puts != 1 || options < 2)
            throw new ProbeFailure("SELF_TEST_CORS_BLOCK_FAILED");
        Console.WriteLine("{\"SelfTestsPassed\":2,\"Browser\":\"Edge\",\"AllowedPutVerified\":true,\"BlockedPutNotSent\":true}");
        return 0;
    }
}