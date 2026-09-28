using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass, TestCategory("PostgreSQL")]
public sealed class DecisionBrowserTests
{
    [TestMethod]
    public async Task CardDrawerDecisionsStaleFormAndReadOnlyState()
    {
        await using var f=await ProcurementTests.Phase1Fixture.CreateAsync(true,true);
        var pair=await f.IngestMarketplacePairAsync();
        Guid id=(await f.Workspace.TakeToWorkAsync(f.Manager,new(pair.AvitoId),"u03",CancellationToken.None)).CaseId;
        await ProcurementUiScenario.RunDecisionsAsync(f,id,()=>f.IngestChangedAvitoAsync(pair.Agent,pair.Administration,1_800_000));
    }
}

internal static partial class ProcurementUiScenario
{
    internal static async Task RunDecisionsAsync(ProcurementTests.Phase1Fixture f,Guid caseId,Func<Task> changeSource)
    {
        int port=PostgresTests.FreePort(); string origin=$"https://127.0.0.1:{port}";
        using var server=PostgresTests.StartHost("LandErp.Server",f.Sandbox.RuntimeConnection,port,true);
        string images=Path.Combine(FoundationTests.RepositoryRoot(),"artifacts","u03");Directory.CreateDirectory(images);
        using var playwright=await Playwright.CreateAsync();
        await using var browser=await playwright.Chromium.LaunchAsync(new(){Channel="chrome",Headless=true,ChromiumSandbox=true});
        await using var context=await browser.NewContextAsync(new(){IgnoreHTTPSErrors=true,ViewportSize=new(){Width=1440,Height=1000}});
        var page=await context.NewPageAsync();page.SetDefaultTimeout(15000);
        List<string> errors=[];page.PageError+=(_,error)=>errors.Add(error);
        try
        {
            await WaitForLiveAsync(server,page,origin);await LoginAsync(page,origin,"manager-phase1@test.invalid");
            await page.GotoAsync(origin+"/procurement/"+caseId);await ReadyAsync(page);
            await page.GetByRole(AriaRole.Button,new(){Name="Решение по объекту",Exact=true}).ClickAsync();
            var modal=page.Locator("dialog[open]");
            await Assertions.Expect(modal).ToContainTextAsync("Текущий исполнитель: Manager");
            await page.ScreenshotAsync(new(){Path=Path.Combine(images,"manager-card-actions.png")});
            await modal.GetByRole(AriaRole.Button,new(){Name="Передать руководителю",Exact=true}).ClickAsync();
            await modal.Locator("textarea").FillAsync("Проверены документы и подъезд к участку.");
            await modal.GetByLabel("Руководитель закупки",new(){Exact=true}).SelectOptionAsync(f.EmployeeId("head-phase1@test.invalid").ToString());
            var current=await f.Workspace.ReadCardAsync(f.Manager,caseId,CancellationToken.None);
            // The same object is forwarded from another session while this draft is open.
            await f.Workspace.DecideAsync(f.Manager,DecisionPresentationTests.Command(current,ProcurementAction.Forward,f.EmployeeId("head-phase1@test.invalid")),"u03-other-session",CancellationToken.None);
            await modal.GetByRole(AriaRole.Button,new(){Name="Передать руководителю",Exact=true}).ClickAsync();
            await Assertions.Expect(modal).ToContainTextAsync("Данные уже изменены");
            await Assertions.Expect(modal.Locator("textarea")).ToHaveValueAsync("Проверены документы и подъезд к участку.");
            await modal.GetByRole(AriaRole.Button,new(){Name="Обновить состояние решения",Exact=true}).ClickAsync();
            await Assertions.Expect(modal).ToContainTextAsync("Ожидается решение руководителя");
            await Assertions.Expect(modal.GetByRole(AriaRole.Button,new(){Name="Передать руководителю",Exact=true})).ToBeDisabledAsync();
            await Assertions.Expect(modal.Locator("textarea")).ToHaveValueAsync("Проверены документы и подъезд к участку.");
            await page.ScreenshotAsync(new(){Path=Path.Combine(images,"stale-draft-preserved.png")});
            await modal.GetByRole(AriaRole.Button,new(){Name="Закрыть",Exact=true}).ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Button,new(){Name="Решение по объекту",Exact=true})).ToHaveCountAsync(0);
            await page.GotoAsync(origin+"/procurement/"+caseId+"?tab=checks");await ReadyAsync(page);
            await Assertions.Expect(page.Locator(".decision-state:visible")).ToContainTextAsync("Ожидается решение руководителя");
            await Assertions.Expect(page.GetByRole(AriaRole.Button,new(){Name="Проверки",Exact=true})).ToHaveAttributeAsync("aria-current","page");
            await page.ScreenshotAsync(new(){Path=Path.Combine(images,"card-waiting-for-head.png")});
            await page.GotoAsync(origin+"/procurement");await ReadyAsync(page);
            await page.Locator("tbody tr").First.Locator("td").Nth(1).ClickAsync();
            var drawer=page.Locator("aside.drawer.on");
            await drawer.Locator(".decision-state").ScrollIntoViewIfNeededAsync();
            await Assertions.Expect(drawer.Locator(".decision-state")).ToContainTextAsync("Текущий исполнитель: Head");
            await Assertions.Expect(drawer.GetByRole(AriaRole.Button,new(){Name="Передать руководителю",Exact=true})).ToHaveCountAsync(0);
            await page.SetViewportSizeAsync(1024,900);
            await page.ScreenshotAsync(new(){Path=Path.Combine(images,"drawer-waiting-for-head.png")});

            await changeSource();
            await using var headContext=await browser.NewContextAsync(new(){IgnoreHTTPSErrors=true,ViewportSize=new(){Width=1024,Height=900}});
            var headPage=await headContext.NewPageAsync();headPage.SetDefaultTimeout(15000);headPage.PageError+=(_,error)=>errors.Add(error);
            await LoginAsync(headPage,origin,"head-phase1@test.invalid");
            await headPage.GotoAsync(origin+"/procurement");await ReadyAsync(headPage);
            await headPage.Locator("tbody tr").First.Locator("td").Nth(1).ClickAsync();
            var headDrawer=headPage.Locator("aside.drawer.on");
            await headDrawer.Locator(".decision-state").ScrollIntoViewIfNeededAsync();
            await Assertions.Expect(headDrawer.GetByRole(AriaRole.Button,new(){Name="Продолжить работу",Exact=true})).ToBeDisabledAsync();
            await Assertions.Expect(headDrawer).ToContainTextAsync("Источники изменились после передачи");
            await headPage.ScreenshotAsync(new(){Path=Path.Combine(images,"head-drawer-source-block.png")});
            await headDrawer.GetByRole(AriaRole.Button,new(){Name="Посмотреть источники",Exact=true}).ClickAsync();
            await Assertions.Expect(headPage.GetByRole(AriaRole.Button,new(){Name="Источники",Exact=true})).ToHaveAttributeAsync("aria-current","page");
            await headPage.GetByRole(AriaRole.Button,new(){Name="Решение по объекту",Exact=true}).ClickAsync();
            await headPage.Locator("dialog[open]").GetByRole(AriaRole.Button,new(){Name="Вернуть менеджеру",Exact=true}).ClickAsync();
            var headModal=headPage.Locator("dialog[open]");
            await headModal.Locator("textarea").Nth(0).FillAsync("Обновилась цена источника.");
            await headModal.Locator("textarea").Nth(1).FillAsync("Проверить новую цену перед согласованием.");
            await headModal.GetByRole(AriaRole.Button,new(){Name="Вернуть менеджеру",Exact=true}).ClickAsync();
            await Assertions.Expect(headModal).ToHaveCountAsync(0);
            Assert.AreEqual("returned",(await f.Workspace.ReadCardAsync(f.Manager,caseId,CancellationToken.None)).Item.Stage);
            await using(var db=f.Sandbox.Context())
            {
                var settings=await db.EmployeeAccessSettings.SingleAsync(x=>x.EmployeeId==f.ManagerEmployeeId);
                settings.ProcurementAccess=ProcurementAccessLevel.Read;await db.SaveChangesAsync();
            }
            await page.GotoAsync(origin+"/procurement/"+caseId+"?tab=checks");await ReadyAsync(page);
            await Assertions.Expect(page.Locator(".decision-state:visible")).ToContainTextAsync("только для просмотра");
            await Assertions.Expect(page.GetByRole(AriaRole.Button,new(){Name="Решение по объекту",Exact=true})).ToHaveCountAsync(0);
            await page.ScreenshotAsync(new(){Path=Path.Combine(images,"card-read-only.png")});
            Assert.AreEqual(0,errors.Count,string.Join("\n",errors));
        }
        catch { await page.ScreenshotAsync(new(){Path=Path.Combine(images,"failure.png")});throw; }
        finally { if(!server.HasExited){server.Kill(true);await server.WaitForExitAsync();} }
    }
}
