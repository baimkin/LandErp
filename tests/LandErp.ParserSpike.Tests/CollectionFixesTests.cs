using LandErp.ParserSpike.LocalCollection;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.ParserSpike.Tests;

[TestClass]
[DoNotParallelize]
public sealed class CollectionFixesTests
{
    [TestMethod]
    public void PublicSeoMapAndUnknownFiltersAreSavedWithoutLosingGeography()
    {
        string root = Path.Combine(Path.GetTempPath(), "LandErp-Fixes", Guid.NewGuid().ToString("N"));
        LocalStore store = new(Path.Combine(root, "data.sqlite"));
        store.SaveLink("SEO", "https://zvenigorod.cian.ru/kupit-zemelniy-uchastok-moskovskaya-oblast-odincovskiy-gorodskoy-okrug/");
        SearchLink map = store.SaveLink("Map", "https://www.cian.ru/cat.php?bbox=55.45%2C37.03%2C55.66%2C37.91&center=55.55%2C37.47&zoom=12&land_status[0]=2&new_filter=abc");
        StringAssert.Contains(map.Url, "bbox=55.45%2C37.03%2C55.66%2C37.91");
        StringAssert.Contains(map.Url, "new_filter=abc");
        store.SaveLink("Avito", "https://www.avito.ru/pushkino/zemelnye_uchastki?new_filter=abc");
        store.SaveLink("Detail", "https://www.cian.ru/sale/suburban/12345678/");
        Assert.AreEqual(4, store.Links().Length);
        Assert.IsTrue(SearchUrls.Normalize("https://www.cian.ru/sale/suburban/12345678/").Warnings.Length > 0);
        Assert.IsTrue(SearchUrls.SameSearch("https://www.avito.ru/pushkino/nedvizhimost?q=земля", "https://www.avito.ru/pushkino/zemelnye_uchastki?q=земля&localPriority=0&p=2", SourceSite.Avito));
        Assert.IsTrue(SearchUrls.SameSearch(
            "https://www.avito.ru/korolev/zemelnye_uchastki/prodam-ASgBAgICAUSWA9oQ?drawId=one&f=filter&localPriority=0&map=polygon&s=1044",
            "https://www.avito.ru/korolev/zemelnye_uchastki/prodam-ASgBAgICAUSWA9oQ?drawId=one&f=filter&map=polygon",
            SourceSite.Avito));
        Assert.IsFalse(SearchUrls.SameSearch(map.Url, map.Url.Replace("zoom=12", "zoom=10", StringComparison.Ordinal), SourceSite.Cian));
    }

    [TestMethod]
    public void DiagnosticsKeepOperationalContextAndRedactOpaqueUrls()
    {
        string root = Path.Combine(Path.GetTempPath(), "LandErp-Diagnostics", Guid.NewGuid().ToString("N"));
        LocalStore store = new(Path.Combine(root, "data.sqlite"));
        store.SaveLink("Search", "https://www.avito.ru/pushkino/zemelnye_uchastki?q=земля");
        string batch = store.StartBatch(new());
        CollectionJob job = store.Claim(batch, SourceSite.Avito, "Avito 1")!;
        DiagnosticJournal journal = new(Path.Combine(root, "events.jsonl"));
        journal.Write(job, 2, "Переход", "Ошибка", 123,
            expectedUrl: job.Url + "&opaque=private-value", actualUrl: job.Url + "&token=secret-value", errorType: "TimeoutException");
        DiagnosticEvent entry = journal.Read().Single();
        Assert.AreEqual(job.Id, entry.JobId); Assert.AreEqual(123L, entry.DurationMilliseconds);
        string text = File.ReadAllText(journal.Path);
        Assert.IsFalse(text.Contains("private-value", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("secret-value", StringComparison.Ordinal));
        StringAssert.Contains(entry.ExpectedUrl, "REDACTED");
        StringAssert.Contains(entry.ActualUrl, "URL_REDACTED");
    }

    [TestMethod]
    public async Task SpaPaginationWaitsForNewCardsWithoutDocumentNavigation()
    {
        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync(new() { Channel = "chrome", Headless = false, ChromiumSandbox = true });
        await using IBrowserContext context = await browser.NewContextAsync();
        const string html = """
            <h1>Земельные участки</h1><div data-marker="catalog-serp" id="cards">
            <div data-marker="item" data-item-id="12345678"><a data-marker="item-title" href="/pushkino/zemelnye_uchastki/uchastok_12345678">Участок 10 сот.</a>
            <span data-marker="item-price">1 000 000 ₽</span></div></div>
            <div data-marker="pagination"><a data-marker="pagination-button/page(2)" href="?q=земля&p=2">2</a></div>
            <script>document.querySelector('[data-marker="pagination"] a').onclick = e => {
                e.preventDefault(); history.pushState(null,'',e.currentTarget.href);
                setTimeout(() => document.getElementById('cards').innerHTML =
                    '<div data-marker="item" data-item-id="87654321"><a data-marker="item-title" href="/pushkino/zemelnye_uchastki/uchastok_87654321">Участок 12 сот.</a><span data-marker="item-price">2 000 000 ₽</span></div>',250);
            };</script>
            """;
        await context.RouteAsync("https://www.avito.ru/**", r => r.FulfillAsync(new() { ContentType = "text/html; charset=utf-8", Body = html }));
        IPage page = await context.NewPageAsync();
        await using DomSourcePage adapter = new(page, SourceSite.Avito, operationTimeoutSeconds: 3);
        await adapter.OpenAsync("https://www.avito.ru/pushkino/zemelnye_uchastki?q=земля", CancellationToken.None);
        Assert.AreEqual("12345678", (await adapter.ReadAsync(CancellationToken.None)).Listings.Single().ExternalId);
        await adapter.FollowAsync(await adapter.NextAsync(CancellationToken.None), CancellationToken.None);
        Assert.AreEqual(2, SearchUrls.PageNumber(adapter.CurrentUrl));
        Assert.AreEqual("87654321", (await adapter.ReadAsync(CancellationToken.None)).Listings.Single().ExternalId);
    }
}
