using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using LandErp.Application.Foundation;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.Collection.Contracts;
using LandErp.Application.Modules.Collection.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Collector.Contracts.V1;
using LandErp.Infrastructure.Modules.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass, DoNotParallelize, TestCategory("PostgreSQL"), TestCategory("Browser")]
public sealed class ParsingWorkspaceBrowserTests
{
    private static readonly string[] Tabs = ["Главная", "Зоны поиска", "Список поисков", "История запусков"];
    [TestMethod, Timeout(240_000)]
    public async Task RealPageTabsCausesEditEnqueueRefreshAndPermissions()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        await f.SetExplicitAccessAsync(f.ManagerEmployeeId, EmployeeAccessRules.NoAccess with
        { CollectionAccess = CollectionAccessLevel.Manage, CanManageSearchGroups = true });
        var admin = new CollectionAdministration(f.Factory, TimeProvider.System);
        Guid zone = await admin.CreateGroupAsync(f.Manager, "Апрелевка", 0, "workspace", default);
        for(int i = 1; i <= 10; i++) await admin.CreateGroupAsync(f.Owner, $"Зона {i:00}", i, "workspace", default);
        for(int i = 0; i < 12; i++) await admin.CreateSearchAsync(f.Owner,
            new($"Поиск {i:00}", i % 2 == 0 ? CatalogSource.Avito : CatalogSource.Cian,
                i % 2 == 0 ? "https://www.avito.ru/moskva/zemelnye_uchastki" : "https://www.cian.ru/cat.php", 10, zone), "workspace", default);
        var searches = (await admin.ReadAsync(f.Owner, default)).Searches.OrderBy(s => s.Label).ToArray();
        var primary = searches[0]; var oldPending = searches[2];
        var agent = await admin.CreateAgentAsync(f.Owner, "Офис-02", false, "workspace", default);
        var waitingAgent = await admin.CreateAgentAsync(f.Owner, "Офис-01", false, "workspace", default);
        var gateway = new CollectorGateway(f.Factory, TimeProvider.System);
        await gateway.RegisterAsync(agent, new(1, "workspace-test", [ListingSource.Avito, ListingSource.Cian]), default);
        await gateway.RegisterAsync(waitingAgent, new(1, "workspace-test", [ListingSource.Avito]), default);
        await gateway.HeartbeatAsync(waitingAgent, new(RuntimeState: AgentRuntimeState.AwaitingManualAction, SourceState: SourceRuntimeState.Captcha), default);
        Guid primaryJob = Guid.CreateVersion7();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using(var db = f.Sandbox.Context())
        {
            db.CollectionJobs.Add(new() { Id = primaryJob, OrganizationId = f.OrganizationId, SearchId = primary.Id, AgentId = agent.AgentId,
                State = CollectionJobState.Partial, ResultCode = "Partial", ReasonCode = "END_NOT_CONFIRMED",
                WarningsJson = "[\"AREA_GEOMETRY_CHANGED\"]", CreatedAt = now.AddMinutes(-5), CompletedAt = now.AddMinutes(-4), AcceptedCount = 3, ProcessedCount = 5 });
            db.CollectionJobs.Add(new() { Id = Guid.CreateVersion7(), OrganizationId = f.OrganizationId, SearchId = oldPending.Id,
                State = CollectionJobState.Pending, CreatedAt = now.AddDays(-1) });
            for(int i = 0; i < 105; i++) db.CollectionJobs.Add(new() { Id = Guid.CreateVersion7(), OrganizationId = f.OrganizationId,
                SearchId = searches[1].Id, State = CollectionJobState.Completed, ResultCode = "Success",
                CreatedAt = now.AddMinutes(-200 + i), CompletedAt = now.AddMinutes(-199 + i) });
            await db.SaveChangesAsync();
        }
        var initial = await admin.ReadAsync(f.Manager, default);
        Assert.HasCount(100, initial.Jobs);
        Assert.IsFalse(initial.Jobs.Any(j => j.SearchId == oldPending.Id));
        Assert.AreEqual("Pending", initial.Searches.Single(s => s.Id == oldPending.Id).ActiveRun?.State);
        Assert.IsNull(initial.Searches.Single(s => s.Id == oldPending.Id).LastRun);

        string output = Path.Combine(FoundationTests.RepositoryRoot(), "artifacts", "parsing-workspace", "browser");
        Directory.CreateDirectory(output);
        using RSA rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder(); names.AddDnsName("localhost"); names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        using var cert = request.CreateSelfSigned(now.AddMinutes(-5), now.AddDays(1));
        string certPath = Path.Combine(output, "test.pfx"), password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        await File.WriteAllBytesAsync(certPath, cert.Export(X509ContentType.Pfx, password));
        string[] keys = ["Storage__Root", "Storage__Provider", "Kestrel__Certificates__Default__Path", "Kestrel__Certificates__Default__Password"];
        string?[] prior = keys.Select(Environment.GetEnvironmentVariable).ToArray();
        int port = PostgresTests.FreePort(); string origin = $"https://127.0.0.1:{port}";
        Process host;
        var startupCodes = new System.Collections.Concurrent.ConcurrentQueue<string>();
        try
        {
            Environment.SetEnvironmentVariable(keys[0], output); Environment.SetEnvironmentVariable(keys[1], "Local");
            Environment.SetEnvironmentVariable(keys[2], certPath); Environment.SetEnvironmentVariable(keys[3], password);
            host = PostgresTests.StartHost("LandErp.Server", f.Sandbox.RuntimeConnection, port, true);
            void Capture(object sender, DataReceivedEventArgs args)
            {
                foreach(System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(args.Data ?? "", @"\b[A-Za-z][A-Za-z.]+Exception\b")) startupCodes.Enqueue(match.Value);
                foreach(string code in new[]{"certificate", "Address already in use", "You must install or update .NET", "Storage:Provider", "static web assets", "permission denied", "Could not load", "Kestrel"})
                    if((args.Data ?? "").Contains(code, StringComparison.OrdinalIgnoreCase)) startupCodes.Enqueue(code);
            }
            host.OutputDataReceived += Capture; host.ErrorDataReceived += Capture;
        }
        finally { for(int i = 0; i < keys.Length; i++) Environment.SetEnvironmentVariable(keys[i], prior[i]); }
        using(host)
        try
        {
            using var pw = await Playwright.CreateAsync();
            await using var browser = await pw.Chromium.LaunchAsync(new() { Channel = "msedge", Headless = true });
            await using var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, ViewportSize = new() { Width = 1720, Height = 1100 } });
            var page = await context.NewPageAsync(); page.SetDefaultTimeout(20_000);
            var errors = new List<string>(); page.PageError += (_, error) => errors.Add(error);
            var external = new List<string>(); page.Request += (_, r) => { if(!r.Url.StartsWith(origin, StringComparison.Ordinal) && !r.Url.StartsWith("data:", StringComparison.Ordinal)) external.Add(new Uri(r.Url).Host); };
            for(int i = 0; ; i++)
            {
                if(host.HasExited) Assert.Fail($"Test host exited ({host.ExitCode}); diagnostic classes: {string.Join(",", startupCodes.Distinct())}");
                try { await page.GotoAsync(origin + "/health/live"); break; }
                catch(PlaywrightException) when(i < 50) { await Task.Delay(100); }
            }
            await page.GotoAsync(origin + "/account/login");
            await page.GetByLabel("Логин", new() { Exact = true }).FillAsync("manager-phase1@test.invalid");
            await page.GetByLabel("Пароль", new() { Exact = true }).FillAsync("Synthetic1!PasswordForTests");
            await page.GetByRole(AriaRole.Button, new() { Name = "Войти", Exact = true }).ClickAsync();
            await page.WaitForURLAsync(origin + "/");
            await page.GotoAsync(origin + "/collectors");
            await page.GetByRole(AriaRole.Tab, new() { Name = "Главная" }).WaitForAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Tab)).ToHaveCountAsync(4);
            var issue = page.Locator($".issue-table tr[data-search='{primary.Id}']");
            await Assertions.Expect(issue).ToContainTextAsync("Контур изменился");
            await Assertions.Expect(issue).ToContainTextAsync("Сохранено 3 объявлений");
            Assert.AreEqual(0, await page.GetByRole(AriaRole.Heading, new() { Name = "Зоны поиска", Exact = true }).CountAsync());
            await page.ScreenshotAsync(new() { Path = Path.Combine(output, "home-1720.png"), FullPage = true });
            await page.SetViewportSizeAsync(390, 950);
            await page.WaitForTimeoutAsync(250);
            Assert.IsFalse(await page.EvaluateAsync<bool>("()=>document.documentElement.scrollWidth>innerWidth"));
            await page.ScreenshotAsync(new() { Path = Path.Combine(output, "home-issues-390.png"), FullPage = true });
            await page.SetViewportSizeAsync(1720, 1100);
            await issue.GetByRole(AriaRole.Button, new() { Name = "Изменить ссылку поиска Поиск 00" }).ClickAsync();
            var modal = page.Locator("dialog[open]");
            const string editedUrl = "https://www.avito.ru/moskva/zemelnye_uchastki?f=workspace-updated";
            await modal.GetByLabel("Ссылка на результаты поиска").FillAsync(editedUrl);
            // The production refresh timer must not replace the draft or expected version.
            await page.WaitForTimeoutAsync(16_000);
            await Assertions.Expect(modal.GetByLabel("Ссылка на результаты поиска")).ToHaveValueAsync(editedUrl);
            await modal.GetByRole(AriaRole.Button, new() { Name = "Сохранить и запустить", Exact = true }).ClickAsync();
            await Assertions.Expect(modal).ToHaveCountAsync(0);
            await Assertions.Expect(issue).ToContainTextAsync("повтор уже запущен");
            await Assertions.Expect(issue.GetByRole(AriaRole.Button, new() { Name = "Повторить", Exact = true })).ToBeDisabledAsync();
            var afterSave = (await admin.ReadAsync(f.Manager, default)).Searches.Single(s => s.Id == primary.Id);
            Assert.AreEqual(editedUrl, afterSave.Url); Assert.IsNotNull(afterSave.ActiveRun);
            Assert.AreEqual(primaryJob, afterSave.LastRun!.JobId);
            // Complete only the synthetic test job. The prior error clears on refresh, not on enqueue.
            await using(var db = f.Sandbox.Context())
            {
                var job = await db.CollectionJobs.SingleAsync(j => j.Id == afterSave.ActiveRun.JobId);
                job.State = CollectionJobState.Completed; job.ResultCode = "Success"; job.CompletedAt = now.AddMinutes(1); job.AcceptedCount = 6;
                await db.SaveChangesAsync();
            }
            await page.GetByRole(AriaRole.Button, new() { Name = "Обновить", Exact = true }).ClickAsync();
            await Assertions.Expect(issue).ToHaveCountAsync(0);
            await page.GetByRole(AriaRole.Tab, new() { Name = "Зоны поиска" }).ClickAsync();
            await Assertions.Expect(page.Locator(".zones-table tbody tr")).ToHaveCountAsync(8);
            await page.GetByRole(AriaRole.Button, new() { Name = "Следующая страница зон" }).ClickAsync();
            await Assertions.Expect(page.Locator(".zones-table tbody tr")).ToHaveCountAsync(3);
            await page.GetByLabel("Найти зону", new() { Exact = true }).FillAsync("Апрелевка");
            await page.GetByRole(AriaRole.Button, new() { Name = "12 поисков" }).ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Tab, new() { Name = "Список поисков" })).ToHaveAttributeAsync("aria-selected", "true");
            var primaryRow = page.Locator($".search-table tr[data-search='{primary.Id}']");
            await Assertions.Expect(primaryRow).ToContainTextAsync("Сохранено 6 объявлений");
            await Assertions.Expect(page.Locator($".search-table tr[data-search='{oldPending.Id}'] button").First).ToBeDisabledAsync();
            await page.ScreenshotAsync(new() { Path = Path.Combine(output, "searches-1720.png"), FullPage = true });
            await page.GetByRole(AriaRole.Button, new() { Name = "Следующая страница поисков" }).ClickAsync();
            await Assertions.Expect(page.Locator(".search-table tbody tr")).ToHaveCountAsync(4);
            await page.GetByLabel("Найти поиск", new() { Exact = true }).FillAsync("Поиск 00");
            await Assertions.Expect(page.Locator(".search-table tbody tr")).ToHaveCountAsync(1);
            await page.GetByRole(AriaRole.Tab, new() { Name = "История запусков" }).ClickAsync();
            await page.ScreenshotAsync(new() { Path = Path.Combine(output, "history-1720.png"), FullPage = true });
            await page.GetByLabel("Результат запуска", new() { Exact = true }).SelectOptionAsync("attention");
            await Assertions.Expect(page.Locator(".history-table tbody tr")).ToHaveCountAsync(1);
            await Assertions.Expect(page.Locator(".history-table tbody tr")).ToContainTextAsync("Контур изменился");
            await page.GetByRole(AriaRole.Tab, new() { Name = "Главная" }).FocusAsync();
            await page.Keyboard.PressAsync("ArrowRight");
            await Assertions.Expect(page.GetByRole(AriaRole.Tab, new() { Name = "Зоны поиска" })).ToBeFocusedAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Изменить зону Апрелевка", Exact = true }).ClickAsync();
            await modal.GetByLabel("Название зоны", new() { Exact = true }).FillAsync("Апрелевка · обновлена");
            await modal.GetByRole(AriaRole.Button, new() { Name = "Сохранить", Exact = true }).ClickAsync();
            await Assertions.Expect(modal).ToHaveCountAsync(0);
            Assert.AreEqual("Апрелевка · обновлена", (await admin.ReadAsync(f.Manager, default)).Groups.Single(g => g.Id == zone).Name);
            foreach(int width in new[]{1440, 1024, 390})
            {
                await page.SetViewportSizeAsync(width, 950);
                await page.WaitForTimeoutAsync(250);
                foreach(string tab in Tabs)
                {
                    await page.GetByRole(AriaRole.Tab, new() { Name = tab }).ClickAsync();
                    await page.ScreenshotAsync(new() { Path = Path.Combine(output, $"{Array.IndexOf(Tabs, tab)}-{width}.png"), FullPage = true });
                    string geometry = await page.EvaluateAsync<string>("()=>JSON.stringify([...document.querySelectorAll('body *')].filter(e=>e.getBoundingClientRect().right>innerWidth+1).slice(0,12).map(e=>({tag:e.tagName,css:typeof e.className==='string'?e.className:'svg',width:e.getBoundingClientRect().width,right:e.getBoundingClientRect().right})))");
                    Assert.IsFalse(await page.EvaluateAsync<bool>("()=>document.documentElement.scrollWidth>innerWidth"), $"Overflow at {width}: {tab}; {geometry}");
                }
            }
            // Existing permissions remain authoritative; a fresh read removes operational tabs.
            await f.SetExplicitAccessAsync(f.ManagerEmployeeId, EmployeeAccessRules.NoAccess with { CollectionAccess = CollectionAccessLevel.Read });
            await page.ReloadAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Tab)).ToHaveCountAsync(4);
            Assert.AreEqual(0, await page.GetByRole(AriaRole.Button, new() { Name = "+ Добавить поиск", Exact = true }).CountAsync());
            await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => admin.EnqueueAsync(f.Manager, primary.Id, "readonly-denied", default));
            await f.SetExplicitAccessAsync(f.ManagerEmployeeId, EmployeeAccessRules.NoAccess with { CanManageSearchGroups = true });
            await page.ReloadAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Tab)).ToHaveCountAsync(1);
            await Assertions.Expect(page.GetByRole(AriaRole.Tab)).ToContainTextAsync("Зоны поиска");
            await Assertions.Expect(page.Locator(".zones-table tbody tr")).ToHaveCountAsync(1);
            await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => admin.EnqueueAsync(f.Manager, primary.Id, "denied", default));
            Assert.AreEqual(0, errors.Count, string.Join("\n", errors));
            Assert.AreEqual(0, external.Count, "No marketplace requests are part of this page test.");
        }
        finally { if(!host.HasExited) { host.Kill(true); await host.WaitForExitAsync(); } }
    }
}
