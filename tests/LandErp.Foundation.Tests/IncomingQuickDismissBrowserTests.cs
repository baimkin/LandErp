using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using LandErp.Application.Modules.Catalog.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass, DoNotParallelize, TestCategory("PostgreSQL"), TestCategory("Browser")]
public sealed class IncomingQuickDismissBrowserTests
{
    [TestMethod, Timeout(180_000)]
    public async Task ConfirmedDismissKeepsMountedTablePositionOpensNextAndRefillsAcrossPageBoundary()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        await using (var db = f.Sandbox.Context())
        {
            var now = DateTimeOffset.UtcNow;
            for (int i = 0; i < 31; i++) db.Listings.Add(new()
            {
                Id = Guid.CreateVersion7(), OrganizationId = f.OrganizationId,
                Source = CatalogSource.Manual, IngestionKind = CatalogIngestionKind.Employee,
                CreatedByEmployeeId = f.ManagerEmployeeId, Title = $"Быстрый отказ {i:00}", Location = "Москва",
                Price = 1_000_000m + i * 10_000m, AreaSquareMeters = 1000m,
                RecordedAt = now, ReceivedAt = now, ChangedAt = now, Provenance = "Тест"
            });
            await db.SaveChangesAsync();
        }
        string output = Path.Combine(FoundationTests.RepositoryRoot(), "artifacts", "incoming-quick-dismiss", "browser");
        Directory.CreateDirectory(output);
        using RSA rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder(); names.AddDnsName("localhost"); names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        string certPath = Path.Combine(output, "test.pfx"), password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        await File.WriteAllBytesAsync(certPath, cert.Export(X509ContentType.Pfx, password));
        string[] keys = ["Storage__Root", "Storage__Provider", "Kestrel__Certificates__Default__Path", "Kestrel__Certificates__Default__Password"];
        string?[] prior = keys.Select(Environment.GetEnvironmentVariable).ToArray();
        int port = PostgresTests.FreePort(); string origin = $"https://127.0.0.1:{port}";
        Process host;
        try
        {
            Environment.SetEnvironmentVariable(keys[0], output); Environment.SetEnvironmentVariable(keys[1], "Local");
            Environment.SetEnvironmentVariable(keys[2], certPath); Environment.SetEnvironmentVariable(keys[3], password);
            host = PostgresTests.StartHost("LandErp.Server", f.Sandbox.RuntimeConnection, port, true);
        }
        finally { for (int i = 0; i < keys.Length; i++) Environment.SetEnvironmentVariable(keys[i], prior[i]); }
        using (host)
        try
        {
            using var pw = await Playwright.CreateAsync();
            await using var browser = await pw.Chromium.LaunchAsync(new() { Channel = "msedge", Headless = true });
            await using var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, ViewportSize = new() { Width = 1440, Height = 900 } });
            var page = await context.NewPageAsync(); page.SetDefaultTimeout(15_000);
            var errors = new List<string>(); page.PageError += (_, error) => errors.Add(error);
            for (int i = 0; ; i++)
            {
                if (host.HasExited) Assert.Fail($"Local test host exited ({host.ExitCode}).");
                try { await page.GotoAsync(origin + "/health/live"); break; }
                catch (PlaywrightException) when (i < 50) { await Task.Delay(100); }
            }
            await page.GotoAsync(origin + "/account/login");
            await page.GetByLabel("Логин", new() { Exact = true }).FillAsync("manager-phase1@test.invalid");
            await page.GetByLabel("Пароль", new() { Exact = true }).FillAsync("Synthetic1!PasswordForTests");
            await page.GetByRole(AriaRole.Button, new() { Name = "Войти", Exact = true }).ClickAsync();
            await page.WaitForURLAsync(origin + "/");
            await page.GotoAsync(origin + "/incoming");
            await page.GetByRole(AriaRole.Button, new() { Name = "Все объявления", Exact = true }).ClickAsync();
            Assert.AreEqual("true", await page.Locator(".incoming-v2").GetAttributeAsync("data-quick-dismiss"));
            var rows = page.Locator("tr[data-listing-id]");
            await Assertions.Expect(rows).ToHaveCountAsync(25);
            string[] firstIds = await rows.EvaluateAllAsync<string[]>("rows=>rows.map(e=>e.dataset.listingId)");
            string dismissedId = firstIds[10], nextId = firstIds[11];
            await rows.Nth(10).ClickAsync();
            var drawer = page.Locator("aside.v2-drawer.open");
            await drawer.GetByRole(AriaRole.Button, new() { Name = "Не подходит", Exact = true }).ClickAsync();
            var modal = page.Locator("dialog[open]");
            await modal.GetByLabel("Причина / подтверждение", new() { Exact = true }).FillAsync("Подъезд не подходит для строительства");
            await page.EvaluateAsync("()=>{window.scrollTo({top:750,left:0,behavior:'instant'});document.querySelector('.v2-table-wrap').scrollLeft=180;window.dismissTable=document.querySelector('.v2-table');}");
            double position = await page.EvaluateAsync<double>("()=>scrollY");
            double horizontal = await page.Locator(".v2-table-wrap").EvaluateAsync<double>("e=>e.scrollLeft");
            Assert.AreEqual(750, position, 1);
            await page.ScreenshotAsync(new() { Path = Path.Combine(output, "before-confirm-1440.png") });
            await modal.GetByRole(AriaRole.Button, new() { Name = "Подтвердить", Exact = true }).ClickAsync();
            await Assertions.Expect(modal).ToHaveCountAsync(0);
            await Assertions.Expect(page.Locator($"tr[data-listing-id='{dismissedId}']")).ToHaveCountAsync(0);
            await Assertions.Expect(rows).ToHaveCountAsync(25);
            await Assertions.Expect(drawer.Locator("h2")).ToHaveTextAsync(await page.Locator($"tr[data-listing-id='{nextId}'] .v2-object > strong").InnerTextAsync());
            try { await page.WaitForFunctionAsync("p=>Math.abs(scrollY-p)<3", position); }
            catch (TimeoutException)
            {
                await page.ScreenshotAsync(new() { Path = Path.Combine(output, "position-failure-1440.png") });
                Assert.Fail(await page.EvaluateAsync<string>("()=>JSON.stringify({current:scrollY,errors:document.querySelector('#blazor-error-ui')?.style.display})"));
            }
            Assert.AreEqual(horizontal, await page.Locator(".v2-table-wrap").EvaluateAsync<double>("e=>e.scrollLeft"), 2);
            Assert.IsTrue(await page.EvaluateAsync<bool>("()=>window.dismissTable===document.querySelector('.v2-table')"));
            await page.ScreenshotAsync(new() { Path = Path.Combine(output, "after-confirm-1440.png") });
            // Close the next drawer and inspect the next ordinary page: all remaining 30 IDs occur exactly once.
            await drawer.GetByRole(AriaRole.Button, new() { Name = "Закрыть", Exact = true }).ClickAsync();
            var seen = (await rows.EvaluateAllAsync<string[]>("rows=>rows.map(e=>e.dataset.listingId)")).ToList();
            await page.GetByRole(AriaRole.Button, new() { Name = "Далее", Exact = true }).ClickAsync();
            await Assertions.Expect(rows).ToHaveCountAsync(5);
            seen.AddRange(await rows.EvaluateAllAsync<string[]>("rows=>rows.map(e=>e.dataset.listingId)"));
            Assert.AreEqual(30, seen.Count); Assert.AreEqual(30, seen.Distinct().Count());
            Assert.IsFalse(seen.Contains(dismissedId));
            var saved = await f.Workspace.ReadItemAsync(f.Manager, Guid.Parse(dismissedId), default);
            Assert.AreEqual(CatalogDisposition.Dismissed, saved.Item.Disposition);
            Assert.AreEqual(0, errors.Count, string.Join("\n", errors));
            await page.SetViewportSizeAsync(390, 900);
            Assert.IsFalse(await page.EvaluateAsync<bool>("()=>document.documentElement.scrollWidth>innerWidth"));
            await page.ScreenshotAsync(new() { Path = Path.Combine(output, "remaining-page-390.png") });
        }
        finally { if (!host.HasExited) { host.Kill(true); await host.WaitForExitAsync(); } }
    }
}
