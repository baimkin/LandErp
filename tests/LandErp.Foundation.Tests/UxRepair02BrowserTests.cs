using Microsoft.EntityFrameworkCore;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Application.Modules.Procurement.Contracts;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
public sealed partial class UxRepair02BrowserTests
{
    [TestMethod]
    [TestCategory("PostgreSQL")]
    public async Task SavesNotesCommunicationAndTaskResultInRealBrowser()
    {
        await using var fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false,false);
        var result=await fixture.Workspace.CreateManualCaseAsync(fixture.Manager,new("UX02 · Проверка интерфейса","Московская область",null,14589653.5m,1310m,"Длинное описание объекта для проверки переносов и компоновки.",Guid.CreateVersion7()),"ux02",CancellationToken.None);
        await using(var db=await fixture.Factory.CreateDbContextAsync())
        {
            Guid inspection=Guid.CreateVersion7();
            db.SiteInspections.Add(new(){Id=inspection,OrganizationId=fixture.OrganizationId,PropertyCaseId=result.CaseId,InspectorEmployeeId=fixture.ManagerEmployeeId,Status=InspectionStatus.Completed,OverallConclusion="Участок доступен для осмотра. Уточнить подключение электричества.",CompletedAt=DateTimeOffset.UtcNow});

            foreach(var item in new[]{("Подъезд по дороге","true","true"),("Электричество подключено","false","true"),("Затопление участка","false","false"),("Окружение участка","","")})
            {
                var template=new InspectionTemplateItem{Id=Guid.CreateVersion7(),OrganizationId=fixture.OrganizationId,Key=Guid.CreateVersion7().ToString(),Title=item.Item1,AnswerType=InspectionAnswerType.Boolean,NormalAnswer=item.Item3};
                db.InspectionTemplateItems.Add(template);
                db.SiteInspectionItems.Add(new(){Id=Guid.CreateVersion7(),OrganizationId=fixture.OrganizationId,InspectionId=inspection,TemplateItemId=template.Id,TemplateItemVersion=1,TitleSnapshot=item.Item1,AnswerTypeSnapshot=InspectionAnswerType.Boolean,NormalAnswerSnapshot=item.Item3,Answer=item.Item2,Status=item.Item2.Length==0?InspectionItemStatus.Unanswered:InspectionItemStatus.Answered});
            }
            await db.SaveChangesAsync();
        }
        await ProcurementUiScenario.RunUx02Async(fixture.Sandbox,result.CaseId);
        Assert.AreEqual(14589653.5m,(await fixture.Workspace.ReadCardAsync(fixture.Manager,result.CaseId,CancellationToken.None)).Item.Price);
    }
}
internal static partial class ProcurementUiScenario
{
    internal static async Task RunUx02Async(PostgresSandbox sandbox,Guid caseId)
    {
        int port=PostgresTests.FreePort();string origin=$"https://127.0.0.1:{port}";
        using var server=PostgresTests.StartHost("LandErp.Server",sandbox.RuntimeConnection,port,true);
        string images=Path.Combine(FoundationTests.RepositoryRoot(),"artifacts","ux02");Directory.CreateDirectory(images);
        using var playwright=await Playwright.CreateAsync();
        await using var browser=await playwright.Chromium.LaunchAsync(new(){Channel="chrome",Headless=true,ChromiumSandbox=true});
        await using var context=await browser.NewContextAsync(new(){IgnoreHTTPSErrors=true,ViewportSize=new(){Width=1440,Height=1000}});
        var page=await context.NewPageAsync();page.SetDefaultTimeout(15_000);
        List<string> browserErrors=[];page.PageError+=(_,error)=>browserErrors.Add(error);
        int unloadPrompts=0;page.Dialog+=async(_,dialog)=>{unloadPrompts++;await dialog.DismissAsync();};
        try
        {
            await WaitForLiveAsync(server,page,origin);await LoginAsync(page,origin,"manager-phase1@test.invalid");
            await page.GotoAsync(origin+"/procurement/"+caseId);await ReadyAsync(page);
            await page.GetByRole(AriaRole.Heading,new(){Name="UX02 · Проверка интерфейса",Exact=true}).WaitForAsync();
            var note=page.Locator(".rich-note").Filter(new(){HasText="Рабочие заметки и расчёты"});
            await note.GetByRole(AriaRole.Button,new(){Name="Добавить текст",Exact=true}).ClickAsync();
            await note.GetByRole(AriaRole.Textbox).FillAsync("Заметка UX02: обычный текст и файлы сохраняются вместе.");
            await note.Locator("input[type=file]").SetInputFilesAsync(new FilePayload(){Name="ux02-note.txt",MimeType="text/plain",Buffer=System.Text.Encoding.UTF8.GetBytes("Контрольное вложение UX02")});
            await note.GetByRole(AriaRole.Textbox).EvaluateAsync("el=>{const data=new DataTransfer();data.items.add(new File([Uint8Array.from(atob('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aB9sAAAAASUVORK5CYII='),c=>c.charCodeAt(0))],'clipboard.png',{type:'image/png'}));el.dispatchEvent(new ClipboardEvent('paste',{clipboardData:data,bubbles:true,cancelable:true}));}");
            await note.GetByRole(AriaRole.Button,new(){Name="Сохранить",Exact=true}).ClickAsync();
            await note.GetByRole(AriaRole.Textbox).WaitForAsync(new(){State=WaitForSelectorState.Detached});
            await page.ReloadAsync();await ReadyAsync(page);
            await page.GetByText("Заметка UX02: обычный текст и файлы сохраняются вместе.",new(){Exact=true}).WaitForAsync();
            Assert.AreEqual(1,await note.Locator("img").CountAsync());
            await page.GetByRole(AriaRole.Button,new(){Name="Записать общение",Exact=true}).ClickAsync();
            var dialog=page.Locator("dialog[open]").Last;
            await dialog.GetByLabel("Время",new(){Exact=true}).FillAsync("0930");
            await Assertions.Expect(dialog.GetByLabel("Время",new(){Exact=true})).ToHaveValueAsync("09:30");
            await dialog.GetByRole(AriaRole.Textbox,new(){Name="Что обсудили",Exact=true}).FillAsync("Обсудили документы и согласовали следующий шаг.");
            await dialog.Locator("input[type=file]").SetInputFilesAsync(new FilePayload(){Name="ux02-communication.txt",MimeType="text/plain",Buffer=System.Text.Encoding.UTF8.GetBytes("Материалы общения")});
            await dialog.GetByPlaceholder("Что сделать",new(){Exact=true}).FillAsync("Получить документы UX02");
            await dialog.GetByRole(AriaRole.Button,new(){Name="Сохранить общение и задачу",Exact=true}).ClickAsync();
            await dialog.WaitForAsync(new(){State=WaitForSelectorState.Detached});
            await page.ReloadAsync();await ReadyAsync(page);
            await page.GetByRole(AriaRole.Button,new(){Name="Получить документы UX02",Exact=true}).ClickAsync();
            dialog=page.Locator("dialog[open]").Last;
            await dialog.GetByText("Исходное общение",new(){Exact=false}).WaitForAsync();
            await dialog.GetByRole(AriaRole.Link,new(){Name="ux02-communication.txt ↗",Exact=true}).WaitForAsync();
            await dialog.GetByRole(AriaRole.Button,new(){Name="Выполнить",Exact=true}).ClickAsync();
            await dialog.GetByRole(AriaRole.Textbox,new(){Name="Результат задачи",Exact=true}).FillAsync("Документы получены. Нужна проверка кадастрового плана.");
            await dialog.Locator("input[type=file]").SetInputFilesAsync(new FilePayload(){Name="ux02-result.txt",MimeType="text/plain",Buffer=System.Text.Encoding.UTF8.GetBytes("Результат")});
            await dialog.GetByRole(AriaRole.Button,new(){Name="Выполнить",Exact=true}).ClickAsync();
            await dialog.WaitForAsync(new(){State=WaitForSelectorState.Detached});
            await page.ReloadAsync();await ReadyAsync(page);
            await page.GetByRole(AriaRole.Button,new(){Name="Выполненные (1)",Exact=true}).ClickAsync();
            await page.GetByRole(AriaRole.Button,new(){Name="Получить документы UX02",Exact=true}).ClickAsync();
            dialog=page.Locator("dialog[open]").Last;
            await dialog.GetByText("Документы получены. Нужна проверка кадастрового плана.",new(){Exact=true}).WaitForAsync();
            await page.ScreenshotAsync(new(){Path=Path.Combine(images,"task-result-1440.png")});
            await dialog.GetByRole(AriaRole.Button,new(){Name="Закрыть",Exact=true}).ClickAsync();
            await page.Locator("a[href*='?task=']").First.ClickAsync();
            dialog=page.Locator("dialog[open]").Last;
            await dialog.GetByText("Документы получены. Нужна проверка кадастрового плана.",new(){Exact=true}).WaitForAsync();
            await dialog.GetByRole(AriaRole.Button,new(){Name="Закрыть",Exact=true}).ClickAsync();
            await page.SetViewportSizeAsync(1920,1080);await page.ScreenshotAsync(new(){Path=Path.Combine(images,"case-1920.png"),FullPage=true});
            await page.SetViewportSizeAsync(1024,900);await page.ScreenshotAsync(new(){Path=Path.Combine(images,"case-1024.png"),FullPage=true});
            await page.SetViewportSizeAsync(1440,1000);
            await page.GetByRole(AriaRole.Button,new(){Name="Проверки",Exact=true}).ClickAsync();
            await page.GetByRole(AriaRole.Button,new(){Name="＋ Проверка",Exact=true}).First.ClickAsync();
            var check=page.Locator(".check-entry").Last;
            await check.GetByLabel("Название проверки",new(){Exact=true}).FillAsync("Право собственности сверено");
            await check.GetByLabel("Состояние",new(){Exact=false}).SelectOptionAsync("Passed");
            await check.GetByRole(AriaRole.Textbox,new(){Name="Текст",Exact=true}).FillAsync("Документы соответствуют выписке.");
            await check.GetByRole(AriaRole.Button,new(){Name="Сохранить",Exact=true}).ClickAsync();
            await page.GetByText("Документы соответствуют выписке.",new(){Exact=true}).WaitForAsync();
            await page.ScreenshotAsync(new(){Path=Path.Combine(images,"checks-1440.png")});
            await page.GetByRole(AriaRole.Button,new(){Name="Осмотр",Exact=true}).ClickAsync();
            await page.Locator(".inspection-results").WaitForAsync();
            Assert.AreEqual(1,await page.Locator(".inspection-results .blocker").CountAsync(),"False is a problem only when different from the template normal answer.");
            Assert.AreEqual(0,await page.Locator(".inspection-results").GetByText("false",new(){Exact=true}).CountAsync());
            await page.ScreenshotAsync(new(){Path=Path.Combine(images,"inspection-1440.png")});
            Assert.AreEqual(0,unloadPrompts,"Save must never submit/navigate the page.");
            Assert.AreEqual(0,browserErrors.Count,string.Join("\n",browserErrors));
        }
        catch { await page.ScreenshotAsync(new(){Path=Path.Combine(images,"failure.png"),FullPage=true});throw; }
        finally {if(!server.HasExited){server.Kill(true);await server.WaitForExitAsync();}}
    }
}
