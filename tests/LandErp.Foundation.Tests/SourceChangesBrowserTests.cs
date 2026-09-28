using LandErp.Application.Modules.Collection.Contracts;
using System.Text.Json;
using LandErp.Collector.Contracts.V1;
using LandErp.Infrastructure.Modules.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
public sealed class SourceChangesBrowserTests
{
    [TestMethod, TestCategory("PostgreSQL")]
    public async Task SourceComparisonCardDrawerAndLoadError()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(true, true);
        var pair = await f.IngestMarketplacePairAsync();
        var taken = await f.Workspace.TakeToWorkAsync(f.Manager, new(pair.AvitoId), "u02", CancellationToken.None);
        ListingData initial;
        await using (var db = f.Sandbox.Context())
            initial = JsonSerializer.Deserialize<ListingData>((await db.ListingObservations.SingleAsync(x => x.ListingId == pair.AvitoId)).PayloadJson, CollectionJson.Options)!;
        var first = initial with { ObservedAt = initial.ObservedAt.AddMinutes(1), Price = new(FieldPresence.Present, "1900000", 1_900_000m),
            Description = new(FieldPresence.Present, "Участок рядом с лесом.\nПодъезд по грунтовой дороге."), PhotoUrls = ["https://images.test/a.svg", "https://images.test/b.svg"] };
        var second = first with { ObservedAt = first.ObservedAt.AddMinutes(1), Price = new(FieldPresence.Present, "1700000.5", 1_700_000.5m),
            Description = new(FieldPresence.Present, "Участок рядом с лесом.\nПодъезд отремонтирован, электричество по границе."), PhotoUrls = ["https://images.test/b.svg", "https://images.test/missing.svg"] };
        await IngestAsync(f, pair.Agent, pair.Administration, first);
        await IngestAsync(f, pair.Agent, pair.Administration, second);
        int additional=0;
        await ProcurementUiScenario.RunSourceChangesAsync(f.Sandbox, taken.CaseId, pair.AvitoId, async ()=>{additional++;await IngestAsync(f,pair.Agent,pair.Administration,second with{ObservedAt=second.ObservedAt.AddMinutes(additional),Price=new(FieldPresence.Present,"updated",1_700_000m-additional*100_000m)});});
    }

    internal static async Task IngestAsync(ProcurementTests.Phase1Fixture f, AgentCredential agent, CollectionAdministration administration, ListingData data)
    {
        var gateway = new CollectorGateway(f.Factory, TimeProvider.System);
        var search = (await administration.ReadAsync(f.Owner, CancellationToken.None)).Searches.Single(x => x.Label == "Phase1 " + data.Source);
        await administration.EnqueueAsync(f.Owner, search.Id, "u02", CancellationToken.None);
        var work = (await gateway.ClaimAsync(agent, CancellationToken.None))!;
        await gateway.AcceptAsync(agent, new(Guid.CreateVersion7(), work.JobId, work.LeaseId, CollectionOutcome.Success,
            [new(Guid.CreateVersion7().ToString(), data)], true), CancellationToken.None);
    }
}

internal static partial class ProcurementUiScenario
{
    internal static async Task RunSourceChangesAsync(PostgresSandbox sandbox, Guid caseId, Guid sourceId, Func<Task> newChange)
    {
        int port = PostgresTests.FreePort(); string origin = $"https://127.0.0.1:{port}";
        using var server = PostgresTests.StartHost("LandErp.Server", sandbox.RuntimeConnection, port, true);
        string images = Path.Combine(FoundationTests.RepositoryRoot(), "artifacts", "u02"); Directory.CreateDirectory(images);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = "chrome", Headless = true, ChromiumSandbox = true });
        await using var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, ViewportSize = new() { Width = 1440, Height = 1000 } });
        await context.RouteAsync("https://images.test/**", async route =>
        {
            if (route.Request.Url.Contains("missing", StringComparison.Ordinal)) await route.AbortAsync();
            else await route.FulfillAsync(new() { ContentType = "image/svg+xml", Body = "<svg xmlns='http://www.w3.org/2000/svg' width='320' height='180'><rect width='320' height='180' fill='#d7edf5'/><path d='M0 130 L70 70 L180 130 L250 45 L320 130 V180 H0Z' fill='#709867'/></svg>" });
        });
        var page = await context.NewPageAsync(); page.SetDefaultTimeout(15_000);
        List<string> errors = []; page.PageError += (_, error) => errors.Add(error);
        try
        {
            await WaitForLiveAsync(server, page, origin); await LoginAsync(page, origin, "manager-phase1@test.invalid");
            await page.GotoAsync(origin + "/procurement/" + caseId); await ReadyAsync(page);
            await page.Locator(".source-change-notice").ClickAsync();
            var modal = page.Locator("dialog[open].source-changes-modal");
            await Assertions.Expect(modal).ToContainTextAsync("1 900 000 ₽");
            await Assertions.Expect(modal).ToContainTextAsync("1 700 001 ₽");
            await Assertions.Expect(modal).ToContainTextAsync("Подъезд отремонтирован");
            await Assertions.Expect(modal).ToContainTextAsync("Предыдущее значение не сохранено");
            await modal.GetByText("Фото недоступно", new() { Exact = true }).First.WaitForAsync();
            await Assertions.Expect(page.Locator(".source-change-notice")).ToHaveCountAsync(0);
            await page.ScreenshotAsync(new() { Path = Path.Combine(images, "source-comparison-card.png") });
            await newChange();
            await modal.GetByRole(AriaRole.Button, new() { Name = "Закрыть", Exact = true }).ClickAsync();
            await Assertions.Expect(page.Locator(".source-change-notice")).ToHaveCountAsync(1);
            await page.ScreenshotAsync(new(){Path=Path.Combine(images,"source-new-change-after-view.png")});
            await page.Locator(".source-change-notice").ClickAsync();
            await Assertions.Expect(modal).ToContainTextAsync("1 600 000 ₽");
            await Assertions.Expect(page.Locator(".source-change-notice")).ToHaveCountAsync(0);
            await modal.GetByRole(AriaRole.Button,new(){Name="Закрыть",Exact=true}).ClickAsync();
            await page.GotoAsync(origin + "/procurement"); await ReadyAsync(page);
            await page.Locator("tbody tr").First.Locator("td").Nth(1).ClickAsync();
            var drawer = page.Locator("aside.drawer.on"); await drawer.WaitForAsync();
            await drawer.GetByRole(AriaRole.Button, new() { Name = "Посмотреть изменения", Exact = true }).ClickAsync();
            await Assertions.Expect(modal).ToContainTextAsync("Подъезд отремонтирован");
            await page.SetViewportSizeAsync(1024, 900);
            await page.ScreenshotAsync(new() { Path = Path.Combine(images, "source-comparison-drawer.png") });
            await modal.GetByRole(AriaRole.Button, new() { Name = "Закрыть", Exact = true }).ClickAsync();
            await newChange();
            await using (var db = sandbox.Context())
            {
                var id = await db.ListingObservations.Where(x => x.ListingId == sourceId).OrderByDescending(x => x.ObservedAt).Select(x => x.Id).FirstAsync();
                await db.ListingObservations.Where(x => x.Id == id).ExecuteUpdateAsync(set => set.SetProperty(x => x.PayloadJson, "{}"));
            }
            await drawer.GetByRole(AriaRole.Button, new() { Name = "Посмотреть изменения", Exact = true }).ClickAsync();
            await modal.GetByRole(AriaRole.Button, new() { Name = "Повторить загрузку", Exact = true }).WaitForAsync();
            await page.ScreenshotAsync(new() { Path = Path.Combine(images, "source-comparison-error.png") });
            await modal.GetByRole(AriaRole.Button,new(){Name="Закрыть",Exact=true}).ClickAsync();
            await Assertions.Expect(page.Locator(".source-change-notice")).ToHaveCountAsync(1);
            await using(var check=sandbox.Context()){var link=await check.PropertyCaseSourceLinks.SingleAsync(x=>x.PropertyCaseId==caseId&&x.CatalogItemId==sourceId);var revision=await check.Listings.Where(x=>x.Id==sourceId).Select(x=>x.DataRevision).SingleAsync();Assert.IsTrue(link.ViewedDataRevision<revision,"Failed load must not consume new changes.");}
            Assert.AreEqual(0, errors.Count, string.Join("\n", errors));
        }
        catch { await page.ScreenshotAsync(new() { Path = Path.Combine(images, "failure.png") }); throw; }
        finally { if (!server.HasExited) { server.Kill(true); await server.WaitForExitAsync(); } }
    }
}
