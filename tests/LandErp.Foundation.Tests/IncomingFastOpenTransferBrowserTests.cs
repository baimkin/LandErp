using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass, DoNotParallelize, TestCategory("PostgreSQL"), TestCategory("Browser")]
public sealed class IncomingFastOpenTransferBrowserTests
{
    [TestMethod, Timeout(180_000)]
    public async Task LoadingCardCanCloseAndGroupTransferPreservesListScrollAndRecipient()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(true, false);
        await f.SetExplicitAccessAsync(f.ManagerEmployeeId, EmployeeAccessRules.NoAccess with { IncomingAccess = IncomingAccessLevel.Process });
        Guid recipient = f.EmployeeId("manager2-phase1@test.invalid");
        Guid[] ids = new Guid[27];
        await using (var db = f.Sandbox.Context())
        {
            Guid group = Guid.CreateVersion7(); var now = DateTimeOffset.UtcNow;
            db.CatalogObjectGroups.Add(new() { Id = group, OrganizationId = f.OrganizationId, RecordedAt = now, UpdatedAt = now });
            for (int i = 0; i < ids.Length; i++)
            {
                ids[i] = Guid.CreateVersion7();
                db.Listings.Add(new()
                {
                    Id = ids[i], OrganizationId = f.OrganizationId, Source = CatalogSource.Manual,
                    IngestionKind = CatalogIngestionKind.Employee, CreatedByEmployeeId = f.ManagerEmployeeId,
                    Title = $"Быстрый просмотр {i:00}", Location = "Москва", Price = 1_000_000m + i,
                    AreaSquareMeters = 1000m, RecordedAt = now, ReceivedAt = now, ChangedAt = now,
                    Provenance = "Тест", ObjectGroupId = i is 0 or 26 ? group : null
                });
            }
            await db.SaveChangesAsync();
        }
        string output = Path.Combine(FoundationTests.RepositoryRoot(), "artifacts", "incoming-package-a", "browser", "fast-open-transfer");
        Directory.CreateDirectory(output);
        using RSA rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder(); names.AddDnsName("localhost"); names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        string certPath = Path.Combine(output, "test-" + Guid.NewGuid().ToString("N") + ".pfx"), password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
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
            var rows = page.Locator("tr[data-listing-id]");
            await Assertions.Expect(rows).ToHaveCountAsync(25);
            string firstId = await rows.First.GetAttributeAsync("data-listing-id") ?? "";
            Assert.AreEqual(ids[0].ToString(), firstId);
            await page.EvaluateAsync("()=>window.fastIncomingTable=document.querySelector('.v2-table')");
            var drawer = page.Locator("aside.v2-drawer.open");
            // Hold only this disposable listing: the loading UI must appear before a slow write finishes.
            await using (var locked = f.Sandbox.Context())
            await using (var transaction = await locked.Database.BeginTransactionAsync())
            {
                await locked.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM catalog.listings WHERE id={ids[0]} FOR UPDATE");
                await rows.First.ClickAsync();
                await Assertions.Expect(drawer.GetByRole(AriaRole.Status)).ToHaveTextAsync("Открываем объявление…");
                await page.ScreenshotAsync(new() { Path = Path.Combine(output, "card-loading-1440.png") });
                await drawer.GetByRole(AriaRole.Button, new() { Name = "Закрыть", Exact = true }).ClickAsync();
                await transaction.CommitAsync();
            }
            await Assertions.Expect(rows.First.Locator(".v2-review-state")).ToHaveTextAsync("Просмотрено");
            await Assertions.Expect(drawer).ToHaveCountAsync(0);
            await rows.First.ClickAsync();
            await Assertions.Expect(drawer.Locator("h2")).ToHaveTextAsync("Быстрый просмотр 00");
            await page.EvaluateAsync("()=>{window.scrollTo({top:700,left:0,behavior:'instant'});document.querySelector('.v2-table-wrap').scrollLeft=180;}");
            double scroll = await page.EvaluateAsync<double>("()=>scrollY");
            double horizontal = await page.Locator(".v2-table-wrap").EvaluateAsync<double>("e=>e.scrollLeft");
            await drawer.GetByRole(AriaRole.Button, new() { Name = "Передать менеджеру", Exact = true }).ClickAsync();
            var modal = page.Locator("dialog[open]");
            await modal.GetByLabel("Ответственный менеджер", new() { Exact = true }).SelectOptionAsync(recipient.ToString());
            await modal.GetByRole(AriaRole.Button, new() { Name = "Передать менеджеру", Exact = true }).ClickAsync();
            await Assertions.Expect(modal).ToHaveCountAsync(0);
            await Assertions.Expect(drawer).ToContainTextAsync("Объявление уже связано с объектом закупки");
            await Assertions.Expect(drawer.GetByRole(AriaRole.Button, new() { Name = "Передать менеджеру", Exact = true })).ToHaveCountAsync(0);
            await Assertions.Expect(rows).ToHaveCountAsync(25);
            await Assertions.Expect(page.Locator($"tr[data-listing-id='{ids[0]}']")).ToHaveCountAsync(0);
            await Assertions.Expect(page.GetByText("1–25 из 25", new() { Exact = true })).ToHaveCountAsync(1);
            await page.WaitForFunctionAsync("p=>Math.abs(scrollY-p)<3", scroll);
            Assert.AreEqual(horizontal, await page.Locator(".v2-table-wrap").EvaluateAsync<double>("e=>e.scrollLeft"), 2);
            Assert.IsTrue(await page.EvaluateAsync<bool>("()=>window.fastIncomingTable===document.querySelector('.v2-table')"));
            await page.ScreenshotAsync(new() { Path = Path.Combine(output, "transferred-group-1440.png") });
            await drawer.GetByRole(AriaRole.Button, new() { Name = "Закрыть", Exact = true }).ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Далее", Exact = true })).ToBeDisabledAsync();
            var saved = await f.Workspace.ReadItemAsync(f.Manager, ids[0], default);
            var card = await f.Workspace.ReadCardAsync(f.SecondManager, saved.Item.PropertyCaseId!.Value, default);
            Assert.AreEqual(recipient, card.ManagerEmployeeId);
            await using var verify = f.Sandbox.Context();
            Assert.AreEqual(2, await verify.PropertyCaseSourceLinks.CountAsync(link => link.Confirmed));
            Assert.AreEqual(1, await verify.CatalogEvents.CountAsync(item => item.CatalogItemId == ids[0] && item.Kind == CatalogEventKind.ReviewStarted));
            Assert.AreEqual(0, errors.Count, string.Join("\n", errors));
        }
        finally { if (!host.HasExited) { host.Kill(true); await host.WaitForExitAsync(); } }
    }
}
