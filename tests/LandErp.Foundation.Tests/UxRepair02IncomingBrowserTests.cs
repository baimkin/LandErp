using System.Text.Json;
using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Catalog.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace LandErp.Foundation.Tests;
public sealed partial class UxRepair02BrowserTests
{
    private static readonly string[] LeftPhotos=["https://example.invalid/left1.png","https://example.invalid/left2.png"];
    private static readonly string[] RightPhotos=["https://example.invalid/right1.png"];
    private static readonly string[] PhotoReasons=["Похожие фотографии"];
    [TestMethod]
    [TestCategory("PostgreSQL")]
    public async Task IncomingFiltersGalleryAndOneClickTake()
    {
        await using var f=await ProcurementTests.Phase1Fixture.CreateAsync(false,false);
        Guid first=await f.Workspace.CreateManualAsync(f.Manager,new(CatalogSource.Manual,"UX02 входящее A","Химки",14589653.5m,1310m,null,null,null,string.Concat(Enumerable.Repeat("Подробное описание участка с дорогой и коммуникациями. ",40)),"UX02"),"ux-a",CancellationToken.None);
        Guid second=await f.Workspace.CreateManualAsync(f.Manager,new(CatalogSource.Manual,"UX02 входящее B","Дмитров",18000000m,1500m,null,null,null,"Другое описание", "UX02"),"ux-b",CancellationToken.None);
        await using(var db=await f.Factory.CreateDbContextAsync())
        {
            var left=await db.Listings.SingleAsync(x=>x.Id==first);var right=await db.Listings.SingleAsync(x=>x.Id==second);
            left.PhotosJson=JsonSerializer.Serialize(LeftPhotos);
            right.PhotosJson=JsonSerializer.Serialize(RightPhotos);
            db.CatalogDuplicateCandidates.Add(new(){Id=Guid.CreateVersion7(),OrganizationId=f.OrganizationId,ListingId=first,CandidateListingId=second,Score=70,ReasonsJson=JsonSerializer.Serialize(PhotoReasons),PhotoEvidenceJson=JsonSerializer.Serialize(new[]{new DuplicatePhotoEvidence("https://example.invalid/left2.png","https://example.invalid/right1.png",2,false)}),RecordedAt=DateTimeOffset.UtcNow,UpdatedAt=DateTimeOffset.UtcNow});
            await db.SaveChangesAsync();
        }
        await ProcurementUiScenario.RunIncomingUx02Async(f.Sandbox);
    }
}
internal static partial class ProcurementUiScenario
{
    internal static async Task RunIncomingUx02Async(PostgresSandbox sandbox)
    {
        int port=PostgresTests.FreePort();string origin=$"https://127.0.0.1:{port}";
        using var server=PostgresTests.StartHost("LandErp.Server",sandbox.RuntimeConnection,port,true);
        string images=Path.Combine(FoundationTests.RepositoryRoot(),"artifacts","ux02");Directory.CreateDirectory(images);
        using var playwright=await Playwright.CreateAsync();
        await using var browser=await playwright.Chromium.LaunchAsync(new(){Channel="chrome",Headless=true,ChromiumSandbox=true});
        await using var context=await browser.NewContextAsync(new(){IgnoreHTTPSErrors=true,ViewportSize=new(){Width=1440,Height=1000}});
        var page=await context.NewPageAsync();page.SetDefaultTimeout(15_000);
        await page.RouteAsync("https://example.invalid/**",route=>route.FulfillAsync(new(){ContentType="image/svg+xml",Body="<svg xmlns='http://www.w3.org/2000/svg' width='800' height='450'><rect width='800' height='450' fill='#acc9ae'/><path d='M0 450L600 100L800 450' fill='#53795c'/></svg>"}));
        try
        {
            await WaitForLiveAsync(server,page,origin);await LoginAsync(page,origin,"manager-phase1@test.invalid");
            await page.GotoAsync(origin+"/incoming");await ReadyAsync(page);
            await page.GetByRole(AriaRole.Button,new(){Name="Все объявления",Exact=true}).ClickAsync();
            await page.GetByRole(AriaRole.Button,new(){Name="Фильтры",Exact=true}).ClickAsync();
            var money=page.GetByLabel("Цена объекта от, ₽",new(){Exact=true});
            await money.FillAsync("14 589 653,5 ₽");await money.BlurAsync();
            await Assertions.Expect(money).ToHaveValueAsync("14 589 654");
            await money.FillAsync("");await money.BlurAsync();await Assertions.Expect(money).ToHaveValueAsync("");
            await page.GetByLabel("Площадь от, сот.",new(){Exact=true}).FillAsync("13,1");
            await page.GetByLabel("Площадь от, сот.",new(){Exact=true}).BlurAsync();
            await Assertions.Expect(page.GetByLabel("Площадь от, сот.",new(){Exact=true})).ToHaveValueAsync("13,1");
            await page.GetByRole(AriaRole.Button,new(){Name="Сохранить фильтр",Exact=true}).ClickAsync();
            var modal=page.Locator("dialog[open]").Last;await modal.GetByLabel("Название фильтра",new(){Exact=true}).FillAsync("UX02 сохранённый фильтр");
            await modal.GetByRole(AriaRole.Button,new(){Name="Сохранить как новый",Exact=true}).ClickAsync();
            await modal.WaitForAsync(new(){State=WaitForSelectorState.Detached});
            await page.Locator(".v2-context-info").First.ScrollIntoViewIfNeededAsync();
            Assert.IsTrue(await page.Locator(".v2-context-info").First.EvaluateAsync<bool>("el=>parseFloat(getComputedStyle(el).paddingLeft)>=16"));
            Assert.IsTrue(await page.Locator(".v2-result-summary").EvaluateAsync<bool>("el=>parseFloat(getComputedStyle(el).paddingLeft)>=16"));
            await page.EvaluateAsync("window.scrollTo(0,450)");
            await page.ScreenshotAsync(new(){Path=Path.Combine(images,"incoming-filters-1440.png")});
            await page.Locator("tr").Filter(new(){HasText="UX02 входящее A"}).GetByText("UX02 входящее A",new(){Exact=true}).ClickAsync();
            var drawer=page.Locator("aside.v2-drawer.open");
            await drawer.GetByRole(AriaRole.Button,new(){Name="Сравнить и решить",Exact=true}).ClickAsync();
            var compare=page.Locator("dialog.compare[open]");await compare.WaitForAsync();
            Assert.AreEqual("https://example.invalid/left2.png",await compare.Locator(".photo-gallery img").First.GetAttributeAsync("src"));
            await compare.GetByRole(AriaRole.Button,new(){Name="Следующее фото",Exact=true}).ClickAsync();
            Assert.AreEqual("https://example.invalid/right1.png",await compare.Locator(".photo-gallery img").Nth(1).GetAttributeAsync("src"));
            await Assertions.Expect(compare.GetByRole(AriaRole.Button,new(){Name="Это один объект — связать",Exact=true})).ToBeInViewportAsync();
            await page.ScreenshotAsync(new(){Path=Path.Combine(images,"duplicate-comparison-1440.png")});
            await compare.Locator(".v2-compare-grid").EvaluateAsync("el=>el.scrollTop=el.scrollHeight");
            await Assertions.Expect(compare.GetByRole(AriaRole.Button,new(){Name="Это один объект — связать",Exact=true})).ToBeInViewportAsync();
            await page.ScreenshotAsync(new(){Path=Path.Combine(images,"duplicate-comparison-scrolled-1440.png")});
            await compare.GetByRole(AriaRole.Button,new(){Name="Закрыть",Exact=true}).Last.ClickAsync();
            await drawer.GetByRole(AriaRole.Button,new(){Name="Взять в работу",Exact=true}).ClickAsync();
            await drawer.WaitForAsync(new(){State=WaitForSelectorState.Detached});
            Assert.AreEqual(origin+"/incoming",page.Url);
            await page.GetByRole(AriaRole.Link,new(){Name="Открыть →",Exact=true}).WaitForAsync();
            await using var db=sandbox.Context();Assert.AreEqual(1,await db.PropertyCases.CountAsync());
        }
        catch {await page.ScreenshotAsync(new(){Path=Path.Combine(images,"incoming-failure.png"),FullPage=true});throw;}
        finally {if(!server.HasExited){server.Kill(true);await server.WaitForExitAsync();}}
    }
}
