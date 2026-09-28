using System.Text.Json;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Application.Modules.Workflow.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
namespace LandErp.Foundation.Tests;
public sealed partial class UxRepair02BrowserTests
{
    [TestMethod,TestCategory("PostgreSQL")]
    public async Task AcceptanceHistoryPricesDrawerAndNativeZoom()
    {
        await using var f=await ProcurementTests.Phase1Fixture.CreateAsync(true,true);
        var pair=await f.IngestMarketplacePairAsync();
        Guid group=await pair.Administration.CreateGroupAsync(f.Owner,"Химки · участки для сравнения",0,"ux-review",CancellationToken.None);
        await using(var db=f.Sandbox.Context())
        {
            foreach(var search in await db.SearchConfigurations.ToArrayAsync())search.SearchGroupId=group;
            foreach(var listing in await db.Listings.ToArrayAsync())listing.IncludeInCalculation=true;
            (await db.SearchGroupMarketSettings.SingleAsync(x=>x.SearchGroupId==group)).DemandTestPricePerSotka=125000.5m;
            await db.SaveChangesAsync();
        }
        Guid caseId=(await f.Workspace.TakeToWorkAsync(f.Manager,new(pair.AvitoId),"ux-review",CancellationToken.None)).CaseId;
        long version=(await f.ReadCaseAsync(caseId)).Version;
        await f.Workspace.RecordCommunicationAsync(f.Manager,new(new(caseId,version,2000000.5m,1800000m,1900000m,"Звонок","Продавец","Обсудили документы. Согласовали цену после проверки границ.","","","",null,DateTimeOffset.UtcNow.AddMinutes(-5),Guid.CreateVersion7()),new("Получить выписку",DateTimeOffset.UtcNow.AddDays(1),true,f.ManagerEmployeeId)),"ux-review",CancellationToken.None);
        var tasks=await f.Workspace.ReadTasksAsync(f.Manager,caseId,CancellationToken.None);var task=tasks.Tasks.Single(x=>x.SourceCommunication!=null);
        await f.Workspace.ChangeTaskAsync(f.Manager,new(caseId,tasks.CaseVersion,task.Id,task.Version,CaseTaskAction.Save,"Получить и сверить выписку","Проверить право собственности и границы участка. Уточнить доступ и возможность подключения электричества.",WorkTaskType.Documents,task.EmployeeId,task.DueAt,task.DueHasTime,Guid.CreateVersion7()),"ux-review",CancellationToken.None);
        await using(var db=f.Sandbox.Context())
        {
            Guid inspection=Guid.CreateVersion7();
            db.SiteInspections.Add(new(){Id=inspection,OrganizationId=f.OrganizationId,PropertyCaseId=caseId,InspectorEmployeeId=f.ManagerEmployeeId,Status=InspectionStatus.Completed,OverallConclusion="Подъезд доступен. Электричество требует уточнения; затопление не выявлено.",CompletedAt=DateTimeOffset.UtcNow});
            foreach(var item in new[]{("Подъезд по дороге","true","true"),("Электричество подключено","false","true"),("Затопление участка","false","false"),("Окружение","","")})
            {
                var template=new InspectionTemplateItem{Id=Guid.CreateVersion7(),OrganizationId=f.OrganizationId,Key=Guid.CreateVersion7().ToString(),Title=item.Item1,AnswerType=InspectionAnswerType.Boolean,NormalAnswer=item.Item3};
                db.InspectionTemplateItems.Add(template);
                db.SiteInspectionItems.Add(new(){Id=Guid.CreateVersion7(),OrganizationId=f.OrganizationId,InspectionId=inspection,TemplateItemId=template.Id,TemplateItemVersion=1,TitleSnapshot=item.Item1,AnswerTypeSnapshot=InspectionAnswerType.Boolean,NormalAnswerSnapshot=item.Item3,Answer=item.Item2,Status=item.Item2.Length==0?InspectionItemStatus.Unanswered:InspectionItemStatus.Answered});
            }
            await db.SaveChangesAsync();
        }
        await ProcurementUiScenario.RunAcceptanceAsync(f.Sandbox,caseId);
    }
}
internal static partial class ProcurementUiScenario
{
    private static readonly string[] ReviewWindowArgs=["--window-size=1440,1000"];
    internal static async Task RunAcceptanceAsync(PostgresSandbox sandbox,Guid caseId)
    {
        int port=PostgresTests.FreePort();string origin=$"https://127.0.0.1:{port}";
        using var server=PostgresTests.StartHost("LandErp.Server",sandbox.RuntimeConnection,port,true);
        string images=Path.Combine(FoundationTests.RepositoryRoot(),"artifacts","ux02","acceptance");Directory.CreateDirectory(images);
        using var playwright=await Playwright.CreateAsync();
        await using var browser=await playwright.Chromium.LaunchAsync(new(){Channel="chrome",Headless=true,ChromiumSandbox=true});
        await using var context=await browser.NewContextAsync(new(){IgnoreHTTPSErrors=true,ViewportSize=new(){Width=1920,Height=1080}});
        var page=await context.NewPageAsync();page.SetDefaultTimeout(15_000);
        List<string> errors=[];page.PageError+=(_,error)=>errors.Add(error);
        try
        {
            await WaitForLiveAsync(server,page,origin);await LoginAsync(page,origin,"manager-phase1@test.invalid");
            await page.GotoAsync(origin+"/procurement/"+caseId);await ReadyAsync(page);
            await page.Locator(".market-group").GetByText("Химки · участки для сравнения",new(){Exact=true}).WaitForAsync();
            await Assertions.Expect(page.Locator(".price-grid")).ToContainTextAsync("2 000 001 ₽");
            await Assertions.Expect(page.Locator(".group-market-prices")).ToContainTextAsync("125 001 ₽");
            await page.GetByRole(AriaRole.Button,new(){Name="Получить и сверить выписку",Exact=true}).WaitForAsync();
            await page.EvaluateAsync("window.scrollTo(0,0)");
            Assert.AreEqual(0,await page.Locator(".topbar").EvaluateAsync<int>("el=>Math.round(el.getBoundingClientRect().top)"));
            await page.ScreenshotAsync(new(){Path=Path.Combine(images,"case-filled-1920-top.png")});
            await page.EvaluateAsync("window.scrollTo(0,700)");
            Assert.AreEqual(0,await page.Locator(".topbar").EvaluateAsync<int>("el=>Math.round(el.getBoundingClientRect().top)"));
            await page.ScreenshotAsync(new(){Path=Path.Combine(images,"case-filled-1920-scrolled.png")});
            await page.SetViewportSizeAsync(1024,900);await page.EvaluateAsync("window.scrollTo(0,0)");
            await page.ScreenshotAsync(new(){Path=Path.Combine(images,"case-filled-1024-top.png")});
            await page.Locator(".case-pricing").ScrollIntoViewIfNeededAsync();
            await page.ScreenshotAsync(new(){Path=Path.Combine(images,"case-filled-1024-prices.png")});
            await page.SetViewportSizeAsync(1440,1000);
            await page.GetByRole(AriaRole.Button,new(){Name="История",Exact=true}).ClickAsync();
            await page.GetByRole(AriaRole.Heading,new(){Name="История объекта",Exact=true}).WaitForAsync();
            await page.EvaluateAsync("window.scrollTo(0,0)");
            var history=page.Locator("article.surface").Filter(new(){Has=page.GetByRole(AriaRole.Heading,new(){Name="История объекта",Exact=true})});
            await history.Locator("summary").ClickAsync();
            await Assertions.Expect(history.Locator(".case-feed")).ToContainTextAsync("Получить выписку → Получить и сверить выписку");
            await Assertions.Expect(history.Locator(".case-feed")).ToContainTextAsync("Обсудили документы. Согласовали цену после проверки границ.");
            Assert.AreEqual(0,await page.Locator(".business-timeline").CountAsync());
            await page.EvaluateAsync("window.scrollTo(0,0)");await page.ScreenshotAsync(new(){Path=Path.Combine(images,"case-history-1440.png")});
            await history.Locator("a[href*='?task=']").First.ClickAsync();await page.Locator("dialog[open]").Last.GetByText("Проверить право собственности и границы участка.",new(){Exact=false}).WaitForAsync();
            await page.Locator("dialog[open]").Last.GetByRole(AriaRole.Button,new(){Name="Закрыть",Exact=true}).ClickAsync();
            await page.GetByRole(AriaRole.Heading,new(){Name="История объекта",Exact=true}).WaitForAsync();
            await page.GotoAsync(origin+"/procurement");await ReadyAsync(page);
            await page.Locator("tbody tr").First.Locator("td").Nth(1).ClickAsync();
            var drawer=page.Locator("aside.drawer.on");await drawer.WaitForAsync();
            await drawer.GetByRole(AriaRole.Button,new(){Name="＋ Задача",Exact=true}).ClickAsync();
            await drawer.GetByLabel("Что сделать",new(){Exact=true}).FillAsync("Позвонить продавцу и уточнить комплект документов");
            await drawer.GetByRole(AriaRole.Button,new(){Name="Завтра",Exact=true}).ClickAsync();
            await drawer.GetByLabel("Время МСК",new(){Exact=true}).FillAsync("0930");
            await drawer.Locator(".task-editor").ScrollIntoViewIfNeededAsync();
            Assert.IsTrue(await drawer.Locator(".task-editor").EvaluateAsync<bool>("el=>el.scrollWidth<=el.clientWidth+1"));
            await page.ScreenshotAsync(new(){Path=Path.Combine(images,"task-editor-drawer-1440.png")});
            await drawer.Locator(".task-editor").GetByRole(AriaRole.Button,new(){Name="Отмена",Exact=true}).ClickAsync();
            await drawer.GetByRole(AriaRole.Button,new(){Name="Посмотреть результат",Exact=true}).ClickAsync();
            var inspection=page.Locator(".history-modal");await inspection.Locator(".inspection-report-group").First.WaitForAsync();
            Assert.AreEqual(4,await inspection.Locator(".inspection-report-group").CountAsync());
            Assert.AreEqual(1,await inspection.Locator(".inspection-report-item.problem").CountAsync());
            await page.ScreenshotAsync(new(){Path=Path.Combine(images,"inspection-drawer-1440.png")});
            await inspection.GetByRole(AriaRole.Button,new(){Name="Закрыть",Exact=true}).ClickAsync();
            string profile=Path.Combine(images,"zoom-profile-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(profile,"Default"));
            await File.WriteAllTextAsync(Path.Combine(profile,"Default","Preferences"),JsonSerializer.Serialize(new {partition=new{default_zoom_level=new{x=Math.Log(1.25)/Math.Log(1.2)}}}));
            await using var zoom=await playwright.Chromium.LaunchPersistentContextAsync(profile,new(){Channel="chrome",Headless=true,IgnoreHTTPSErrors=true,ViewportSize=ViewportSize.NoViewport,Args=ReviewWindowArgs});
            var zoomPage=await zoom.NewPageAsync();await LoginAsync(zoomPage,origin,"manager-phase1@test.invalid");
            await zoomPage.GotoAsync(origin+"/procurement/"+caseId);await ReadyAsync(zoomPage);
            await zoomPage.GetByRole(AriaRole.Button,new(){Name="Получить и сверить выписку",Exact=true}).WaitForAsync();
            double scale=await zoomPage.EvaluateAsync<double>("window.devicePixelRatio");
            Assert.AreEqual(1.25,scale,.01,"Native browser zoom preference must actually apply.");
            await zoomPage.Locator(".case-pricing").ScrollIntoViewIfNeededAsync();
            Assert.IsTrue(await zoomPage.EvaluateAsync<bool>("document.documentElement.scrollWidth<=innerWidth+1"));
            // Capture the actual browser viewport: Playwright clipping with NoViewport mis-scales native zoom.
            var zoomCdp=await zoom.NewCDPSessionAsync(zoomPage);
            var capture=await zoomCdp.SendAsync("Page.captureScreenshot",new Dictionary<string,object>{{"format","png"},{"captureBeyondViewport",false},{"fromSurface",true}});
            await File.WriteAllBytesAsync(Path.Combine(images,"case-native-zoom125.png"),Convert.FromBase64String(capture!.Value.GetProperty("data").GetString()!));
            await File.WriteAllTextAsync(Path.Combine(images,"viewport-evidence.json"),await zoomPage.EvaluateAsync<string>("JSON.stringify({devicePixelRatio,innerWidth,innerHeight,outerWidth,outerHeight,scrollY,headerTop:document.querySelector('.topbar').getBoundingClientRect().top})"));
            Assert.AreEqual(0,errors.Count,string.Join("\n",errors));
        }
        catch {await page.ScreenshotAsync(new(){Path=Path.Combine(images,"failure.png")});throw;}
        finally{if(!server.HasExited){server.Kill(true);await server.WaitForExitAsync();}}
    }
}
