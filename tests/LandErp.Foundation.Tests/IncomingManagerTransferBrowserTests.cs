using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using LandErp.Application.Foundation;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass, DoNotParallelize, TestCategory("PostgreSQL"), TestCategory("Browser")]
public sealed class IncomingManagerTransferBrowserTests
{
    [TestMethod, Timeout(180_000)]
    public async Task IncomingProcessorExplicitlySelectsResponsibleManagerAndCannotDuplicateOrBypassAccess()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(true, false);
        var processOnly = EmployeeAccessRules.NoAccess with { IncomingAccess = IncomingAccessLevel.Process };
        var recipientAccess = EmployeeAccessRules.NoAccess with { ProcurementAccess = ProcurementAccessLevel.Manager };
        await f.SetExplicitAccessAsync(f.ManagerEmployeeId, processOnly);
        Guid recipientId = f.EmployeeId("manager2-phase1@test.invalid");
        await f.SetExplicitAccessAsync(recipientId, recipientAccess);
        const string title = "Участок для передачи менеджеру";
        Guid listingId = await f.Workspace.CreateManualAsync(f.Manager,
            new(CatalogSource.Other, title, "Химки", 2_500_000m, 1200m, null, null, null, "Передача", "Тест"), "transfer-ui", default);
        const string secondTitle = "Проверка прав получателя";
        Guid secondId = await f.Workspace.CreateManualAsync(f.Manager,
            new(CatalogSource.Other, secondTitle, "Химки", 3_000_000m, 1000m, null, null, null, "Передача", "Тест"), "transfer-ui", default);

        string output = Path.Combine(FoundationTests.RepositoryRoot(), "artifacts", "incoming-manager-transfer");
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
        finally { for(int i = 0; i < keys.Length; i++) Environment.SetEnvironmentVariable(keys[i], prior[i]); }
        using(host)
        try
        {
            using var pw = await Playwright.CreateAsync();
            await using var browser = await pw.Chromium.LaunchAsync(new() { Channel = "msedge", Headless = true });
            await using var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, ViewportSize = new() { Width = 1440, Height = 1000 } });
            var page = await context.NewPageAsync(); page.SetDefaultTimeout(15_000);
            var errors = new List<string>(); page.PageError += (_, error) => errors.Add(error);
            for(int i = 0; ; i++)
            {
                if(host.HasExited) Assert.Fail($"Local test host exited ({host.ExitCode}).");
                try { await page.GotoAsync(origin + "/health/live"); break; }
                catch(PlaywrightException) when(i < 50) { await Task.Delay(100); }
            }
            await page.GotoAsync(origin + "/account/login");
            await page.GetByLabel("Логин", new() { Exact = true }).FillAsync("manager-phase1@test.invalid");
            await page.GetByLabel("Пароль", new() { Exact = true }).FillAsync("Synthetic1!PasswordForTests");
            await page.GetByRole(AriaRole.Button, new() { Name = "Войти", Exact = true }).ClickAsync();
            await page.WaitForURLAsync(origin + "/");
            await page.GotoAsync(origin + "/incoming");
            await page.GetByRole(AriaRole.Button, new() { Name = "Все объявления", Exact = true }).ClickAsync();
            await page.Locator("tr").Filter(new() { HasText = title }).ClickAsync();
            var drawer = page.Locator("aside.v2-drawer.open");
            var modal = page.Locator("dialog[open]");
            await Assertions.Expect(drawer.GetByRole(AriaRole.Button, new() { Name = "Взять в работу", Exact = true })).ToHaveCountAsync(0);
            await drawer.GetByRole(AriaRole.Button, new() { Name = "Передать менеджеру", Exact = true }).ClickAsync();
            var select = modal.GetByLabel("Ответственный менеджер", new() { Exact = true });
            await Assertions.Expect(select).ToHaveValueAsync("");
            var confirm = modal.GetByRole(AriaRole.Button, new() { Name = "Передать менеджеру", Exact = true });
            await Assertions.Expect(confirm).ToBeDisabledAsync();
            await select.SelectOptionAsync(recipientId.ToString());
            await page.ScreenshotAsync(new() { Path = Path.Combine(output, "select-manager-1440.png") });
            await page.SetViewportSizeAsync(390, 950); await page.WaitForTimeoutAsync(250);
            Assert.IsFalse(await page.EvaluateAsync<bool>("()=>document.documentElement.scrollWidth>innerWidth"));
            await page.ScreenshotAsync(new() { Path = Path.Combine(output, "select-manager-390.png") });
            await page.SetViewportSizeAsync(1440, 1000);
            await confirm.ClickAsync();
            await Assertions.Expect(modal).ToHaveCountAsync(0);
            await Assertions.Expect(drawer).ToContainTextAsync("Объявление уже связано с объектом закупки");
            await Assertions.Expect(drawer.GetByRole(AriaRole.Button, new() { Name = "Передать менеджеру", Exact = true })).ToHaveCountAsync(0);
            var item = await f.Workspace.ReadItemAsync(f.Manager, listingId, default);
            Guid caseId = item.Item.PropertyCaseId!.Value;
            var card = await f.Workspace.ReadCardAsync(f.SecondManager, caseId, default);
            Assert.AreEqual(recipientId, card.ManagerEmployeeId);
            await using(var db = f.Sandbox.Context())
            {
                Assert.AreEqual(recipientId, (await db.WorkAssignments.SingleAsync(a => a.ObjectType == "PropertyCase" && a.ObjectId == caseId)).EmployeeId);
                var entry = await db.BusinessTimeline.SingleAsync(e => e.ObjectId == caseId && e.Title == "Передан в закупку");
                Assert.AreEqual(f.ManagerEmployeeId, entry.ActorEmployeeId); Assert.AreEqual(recipientId, entry.TargetEmployeeId);
                Assert.AreEqual(1, await db.AuditEvents.CountAsync(e => e.ObjectId == caseId && e.Action == "CatalogItemTransferredToProcurement"));
            }
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => f.Workspace.TransferToProcurementAsync(f.Manager, new(listingId, recipientId), "duplicate", default));
            Assert.AreEqual(1, await f.CountAsync(db => db.PropertyCases.CountAsync()));
            await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => f.Workspace.ReadCardAsync(f.Manager, caseId, default));

            // Recipient permissions can change while the chooser is open; Server must reject the stale choice.
            await page.GotoAsync(origin + "/incoming");
            await page.GetByRole(AriaRole.Button, new() { Name = "Все объявления", Exact = true }).ClickAsync();
            await page.Locator("tr").Filter(new() { HasText = secondTitle }).ClickAsync();
            await drawer.GetByRole(AriaRole.Button, new() { Name = "Передать менеджеру", Exact = true }).ClickAsync();
            await select.SelectOptionAsync(recipientId.ToString());
            await f.SetExplicitAccessAsync(recipientId, EmployeeAccessRules.NoAccess);
            await confirm.ClickAsync();
            await Assertions.Expect(modal.GetByRole(AriaRole.Alert)).ToContainTextAsync("Нет права выполнить это действие");
            Assert.IsNull((await f.Workspace.ReadItemAsync(f.Manager, secondId, default)).Item.PropertyCaseId);
            Assert.AreEqual(1, await f.CountAsync(db => db.PropertyCases.CountAsync()));

            await f.SetExplicitAccessAsync(f.ManagerEmployeeId, EmployeeAccessRules.NoAccess with { IncomingAccess = IncomingAccessLevel.Read });
            await page.GotoAsync(origin + "/incoming");
            await page.GetByRole(AriaRole.Button, new() { Name = "Все объявления", Exact = true }).ClickAsync();
            await page.Locator("tr").Filter(new() { HasText = secondTitle }).ClickAsync();
            await Assertions.Expect(drawer.GetByRole(AriaRole.Button, new() { Name = "Передать менеджеру", Exact = true })).ToHaveCountAsync(0);
            await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => f.Workspace.ReadProcurementTargetsAsync(f.Manager, default));
            await f.SetExplicitAccessAsync(f.ManagerEmployeeId, processOnly with { ProcurementAccess = ProcurementAccessLevel.Manager });
            await page.GotoAsync(origin + "/incoming");
            await page.GetByRole(AriaRole.Button, new() { Name = "Все объявления", Exact = true }).ClickAsync();
            await page.Locator("tr").Filter(new() { HasText = secondTitle }).ClickAsync();
            await Assertions.Expect(drawer.GetByRole(AriaRole.Button, new() { Name = "Взять в работу", Exact = true })).ToHaveCountAsync(1);
            await Assertions.Expect(drawer.GetByRole(AriaRole.Button, new() { Name = "Передать менеджеру", Exact = true })).ToHaveCountAsync(1);
            Assert.AreEqual(0, errors.Count, string.Join("\n", errors));
        }
        finally { if(!host.HasExited) { host.Kill(true); await host.WaitForExitAsync(); } }
    }
}
