using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Workflow.Domain;
using LandErp.Infrastructure.Modules.Catalog;
using LandErp.Infrastructure.Modules.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace LandErp.Foundation.Tests;

[TestClass, TestCategory("PostgreSQL")]
public sealed class OverviewNavigationBrowserTests
{
    [TestMethod]
    public async Task TilesOpenMatchingSlicesAttentionBreakdownAndReadError()
    {
        await using var f=await ProcurementTests.Phase1Fixture.CreateAsync(false,false);
        var pair=await f.IngestMarketplacePairAsync();
        await using var source=NpgsqlDataSource.Create(f.Sandbox.RuntimeConnection);
        var presets=new IncomingFilterPresetService(source,new EmployeeAccessService(f.Factory));
        await presets.CreateAsync(f.Manager,new(null,"Все предложения",OverviewNavigationTests.Criteria()),CancellationToken.None);
        await presets.CreateAsync(f.Manager,new(null,"До трёх миллионов",OverviewNavigationTests.Criteria(3_000_000)),CancellationToken.None);
        var taken=await f.Workspace.CreateManualCaseAsync(f.Manager,new("Участок у леса","Химки",null,2_000_000m,1500m,"Для проверки обзора",Guid.CreateVersion7()),"o01",CancellationToken.None);
        var tasks=await f.Workspace.ReadTasksAsync(f.Manager,taken.CaseId,CancellationToken.None);
        await f.Workspace.ChangeTaskAsync(f.Manager,new(taken.CaseId,tasks.CaseVersion,null,0,CaseTaskAction.Save,"Связаться с собственником","",WorkTaskType.Call,f.ManagerEmployeeId,DateTimeOffset.UtcNow.AddDays(-1),true,Guid.CreateVersion7()),"o01",CancellationToken.None);
        var card=await f.Workspace.ReadCardAsync(f.Manager,taken.CaseId,CancellationToken.None);
        await f.Workspace.DecideAsync(f.Manager,DecisionPresentationTests.Command(card,ProcurementAction.Forward,f.EmployeeId("head-phase1@test.invalid")),"o01",CancellationToken.None);
        await using(var db=f.Sandbox.Context()) { (await db.Listings.SingleAsync(x=>x.Id==pair.AvitoId)).IncludeInCalculation=true;await db.SaveChangesAsync(); }
        await ProcurementUiScenario.RunOverviewNavigationAsync(f);
        await using var check=f.Sandbox.Context();
        Assert.IsTrue((await check.Listings.SingleAsync(x=>x.Id==pair.AvitoId)).IncludeInCalculation);
    }
}

internal static partial class ProcurementUiScenario
{
    internal static async Task RunOverviewNavigationAsync(ProcurementTests.Phase1Fixture f)
    {
        int port=PostgresTests.FreePort();string origin=$"https://127.0.0.1:{port}";
        using var server=PostgresTests.StartHost("LandErp.Server",f.Sandbox.RuntimeConnection,port,true);
        var images=Path.Combine(FoundationTests.RepositoryRoot(),"artifacts","o01");Directory.CreateDirectory(images);
        using var playwright=await Playwright.CreateAsync();
        await using var browser=await playwright.Chromium.LaunchAsync(new(){Channel="chrome",Headless=true,ChromiumSandbox=true});
        await using var context=await browser.NewContextAsync(new(){IgnoreHTTPSErrors=true,ViewportSize=new(){Width=1440,Height=1000}});
        var page=await context.NewPageAsync();page.SetDefaultTimeout(15000);
        List<string> errors=[];page.PageError+=(_,error)=>errors.Add(error);
        try
        {
            await WaitForLiveAsync(server,page,origin);await LoginAsync(page,origin,"manager-phase1@test.invalid");
            var stats=page.Locator("a.overview-stat");await Assertions.Expect(stats).ToHaveCountAsync(3);
            await Assertions.Expect(stats.Nth(0).Locator("strong")).ToHaveTextAsync("2");
            await Assertions.Expect(stats.Nth(1).Locator("strong")).ToHaveTextAsync("1");
            await Assertions.Expect(stats.Nth(2).Locator("strong")).ToHaveTextAsync("1");
            Assert.AreEqual(0,await stats.Locator("button,a,input,select").CountAsync());
            await page.ScreenshotAsync(new(){Path=Path.Combine(images,"overview-1440.png")});
            await stats.Nth(0).GetByText("Непросмотренные входящие",new(){Exact=true}).ClickAsync();
            await Assertions.Expect(page.Locator(".v2-lead").Filter(new(){HasText="объединение сохранённых фильтров"})).ToContainTextAsync("объявлений: 2");
            await Assertions.Expect(page.GetByRole(AriaRole.Button,new(){Name="По сохранённым фильтрам",Exact=true})).ToHaveAttributeAsync("aria-pressed","true");
            await page.ScreenshotAsync(new(){Path=Path.Combine(images,"incoming-matching-slice.png")});
            await page.GotoAsync(origin);await ReadyAsync(page);
            await stats.Nth(1).ClickAsync(new(){Position=new(){X=6,Y=6}});
            await Assertions.Expect(page.Locator(".queue-scope")).ToContainTextAsync("Активные объекты");
            await Assertions.Expect(page.Locator(".queue-scope")).ToContainTextAsync("объектов: 1");
            await page.GotoAsync(origin);await ReadyAsync(page);
            await stats.Nth(2).FocusAsync();await Assertions.Expect(stats.Nth(2)).ToBeFocusedAsync();
            await stats.Nth(2).PressAsync("Enter");
            await Assertions.Expect(page.Locator(".queue-scope")).ToContainTextAsync("Ждут решения руководителя · объектов: 1");
            await page.ScreenshotAsync(new(){Path=Path.Combine(images,"waiting-matching-slice.png")});
            await page.GotoAsync(origin);await ReadyAsync(page);
            var popup=await page.RunAndWaitForPopupAsync(()=>stats.Nth(0).ClickAsync(new(){Modifiers=[KeyboardModifier.Control]}));
            await Assertions.Expect(popup.Locator(".v2-lead").Filter(new(){HasText="объединение сохранённых фильтров"})).ToContainTextAsync("объявлений: 2");
            await popup.CloseAsync();
            await page.SetViewportSizeAsync(1024,900);
            await page.ScreenshotAsync(new(){Path=Path.Combine(images,"overview-1024.png")});
            await Assertions.Expect(page.GetByText("Требуют внимания",new(){Exact=true})).ToHaveCountAsync(0);
            // Simulate an unavailable read on this disposable DB only: no fake zero tiles.
            await using(var db=f.Sandbox.Context())
            {
                var role=new NpgsqlConnectionStringBuilder(f.Sandbox.RuntimeConnection).Username!;
                using var quote=new NpgsqlCommandBuilder();
                await db.Database.OpenConnectionAsync();
                await using var revoke=new NpgsqlCommand("REVOKE SELECT ON catalog.incoming_filter_presets FROM "+quote.QuoteIdentifier(role),(NpgsqlConnection)db.Database.GetDbConnection());
                await revoke.ExecuteNonQueryAsync();
            }
            await page.GotoAsync(origin);await ReadyAsync(page);
            await Assertions.Expect(page.Locator(".overview-state[role='alert']")).ToContainTextAsync("Не удалось загрузить данные");
            await Assertions.Expect(stats).ToHaveCountAsync(0);
            await page.ScreenshotAsync(new(){Path=Path.Combine(images,"overview-read-error.png")});
            Assert.AreEqual(0,errors.Count,string.Join("\n",errors));
        }
        catch { await page.ScreenshotAsync(new(){Path=Path.Combine(images,"failure.png")});throw; }
        finally { if(!server.HasExited){server.Kill(true);await server.WaitForExitAsync();} }
    }
}
