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
public sealed class ParsingRetryBrowserTests
{
    [TestMethod, Timeout(240_000)]
    public async Task IndependentRetriesShowSendingQueuedRunningAndPausedWithoutReload()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        await f.SetExplicitAccessAsync(f.ManagerEmployeeId, EmployeeAccessRules.NoAccess with { CollectionAccess = CollectionAccessLevel.Manage });
        var admin = new CollectionAdministration(f.Factory, TimeProvider.System);
        for(int i=0;i<3;i++) await admin.CreateSearchAsync(f.Owner,
            new($"Повтор {i}",CatalogSource.Avito,"https://www.avito.ru/moskva/zemelnye_uchastki",10),"retry-test",default);
        var searches=(await admin.ReadAsync(f.Owner,default)).Searches.OrderBy(s=>s.Label).ToArray();
        var first=searches[0]; var second=searches[1]; var paused=searches[2];
        var agent=await admin.CreateAgentAsync(f.Owner,"Тестовый парсер",false,"retry-test",default);
        DateTimeOffset now=DateTimeOffset.UtcNow;
        await using(var db=f.Sandbox.Context())
        {
            (await db.SearchConfigurations.SingleAsync(s=>s.Id==paused.Id)).Enabled=false;
            foreach(var search in searches) db.CollectionJobs.Add(new() {Id=Guid.CreateVersion7(),OrganizationId=f.OrganizationId,
                SearchId=search.Id,CreatedAt=now.AddHours(-1),CompletedAt=now.AddMinutes(-50),State=CollectionJobState.Failed,
                ResultCode="SourceError",ReasonCode="NETWORK_TIMEOUT"});
            await db.SaveChangesAsync();
        }
        string output = Path.Combine(FoundationTests.RepositoryRoot(), "artifacts", "parsing-retry-state", "browser");
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
            var a=page.Locator($".issue-table tr[data-search='{first.Id}']");
            var b=page.Locator($".issue-table tr[data-search='{second.Id}']");
            var p=page.Locator($".issue-table tr[data-search='{paused.Id}']");
            await Assertions.Expect(a.GetByRole(AriaRole.Button,new(){Name="Повторить",Exact=true})).ToBeEnabledAsync();
            await Assertions.Expect(p.GetByRole(AriaRole.Button,new(){Name="На паузе",Exact=true})).ToBeDisabledAsync();
            await Assertions.Expect(p).ToContainTextAsync("Включите «Поиск активен»");
            await using var locked=await f.Factory.CreateDbContextAsync();
            await using var transaction=await locked.Database.BeginTransactionAsync();
            await locked.SearchConfigurations.FromSqlInterpolated($"SELECT * FROM collection.search_configurations WHERE id={first.Id} FOR UPDATE").LoadAsync();
            await a.GetByRole(AriaRole.Button,new(){Name="Повторить",Exact=true}).ClickAsync();
            await Assertions.Expect(a.GetByRole(AriaRole.Button,new(){Name="Отправляем…",Exact=true})).ToBeDisabledAsync();
            await Assertions.Expect(b.GetByRole(AriaRole.Button,new(){Name="Повторить",Exact=true})).ToBeEnabledAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Button,new(){Name="+ Добавить поиск",Exact=true})).ToBeEnabledAsync();
            await b.GetByRole(AriaRole.Button,new(){Name="Повторить",Exact=true}).ClickAsync();
            await Assertions.Expect(b.GetByRole(AriaRole.Button,new(){Name="В очереди",Exact=true})).ToBeDisabledAsync();
            await Assertions.Expect(a.GetByRole(AriaRole.Button,new(){Name="Отправляем…",Exact=true})).ToBeDisabledAsync();
            await page.ScreenshotAsync(new(){Path=Path.Combine(output,"independent-sending-1720.png")});
            await transaction.CommitAsync();
            await Assertions.Expect(a.GetByRole(AriaRole.Button,new(){Name="В очереди",Exact=true})).ToBeDisabledAsync();
            await using(var db=f.Sandbox.Context())
            {
                Assert.AreEqual(1,await db.CollectionJobs.CountAsync(j=>j.SearchId==first.Id&&j.State==CollectionJobState.Pending));
                Assert.AreEqual(1,await db.CollectionJobs.CountAsync(j=>j.SearchId==second.Id&&j.State==CollectionJobState.Pending));
                var job=await db.CollectionJobs.SingleAsync(j=>j.SearchId==first.Id&&j.State==CollectionJobState.Pending);
                job.State=CollectionJobState.Leased;job.AgentId=agent.AgentId;job.LeaseExpiresAt=DateTimeOffset.UtcNow.AddMinutes(5);
                await db.SaveChangesAsync();
            }
            await page.GetByRole(AriaRole.Button,new(){Name="Обновить",Exact=true}).ClickAsync();
            await Assertions.Expect(a.GetByRole(AriaRole.Button,new(){Name="Выполняется",Exact=true})).ToBeDisabledAsync();
            await Assertions.Expect(b.GetByRole(AriaRole.Button,new(){Name="В очереди",Exact=true})).ToBeDisabledAsync();
            await page.ScreenshotAsync(new(){Path=Path.Combine(output,"queued-running-paused-1720.png")});
            await page.GetByRole(AriaRole.Tab,new(){Name="Список поисков"}).ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Tab,new(){Name="Список поисков"})).ToHaveAttributeAsync("aria-selected","true");
            await Assertions.Expect(page.Locator($".search-table tr[data-search='{first.Id}']")).ToContainTextAsync("Выполняется");
            await Assertions.Expect(page.Locator($".search-table tr[data-search='{paused.Id}']")).ToContainTextAsync("На паузе");
            await page.SetViewportSizeAsync(390,950);
            await page.GetByRole(AriaRole.Tab,new(){Name="Главная"}).ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Tab,new(){Name="Главная"})).ToHaveAttributeAsync("aria-selected","true");
            await a.ScrollIntoViewIfNeededAsync();
            await page.ScreenshotAsync(new(){Path=Path.Combine(output,"retry-statuses-390.png")});
            Assert.IsFalse(await page.EvaluateAsync<bool>("()=>document.documentElement.scrollWidth>innerWidth"));
            await page.SetViewportSizeAsync(1720,1100);
            await using(var db=f.Sandbox.Context())
            {
                var job=await db.CollectionJobs.SingleAsync(j=>j.SearchId==first.Id&&j.State==CollectionJobState.Leased);
                job.State=CollectionJobState.Failed;job.CompletedAt=DateTimeOffset.UtcNow;job.ResultCode="SourceError";job.ReasonCode="NETWORK_TIMEOUT";
                await db.SaveChangesAsync();
            }
            // The ordinary 15-second refresh must restore the action without browser reload.
            await Assertions.Expect(a.GetByRole(AriaRole.Button,new(){Name="Повторить",Exact=true})).ToBeEnabledAsync(new(){Timeout=22000});
            await Assertions.Expect(b.GetByRole(AriaRole.Button,new(){Name="В очереди",Exact=true})).ToBeDisabledAsync();
            Assert.AreEqual(0,errors.Count,string.Join("\n",errors));
            Assert.AreEqual(0,external.Count);
        }
        finally { if(!host.HasExited) {host.Kill(true);await host.WaitForExitAsync();} }
    }
}
