using LandErp.ParserSpike.Application;
using LandErp.ParserSpike.Avito;
using LandErp.ParserSpike.Contracts;
using LandErp.ParserSpike.Serialization;
using LandErp.ParserSpike.Storage;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LandErp.ParserSpike.Browser;

namespace LandErp.ParserSpike.Tests;

[TestClass]
public sealed class CollectionStorageTests
{
    private static string DatabasePath() => Path.Combine(Path.GetTempPath(), "LandErp-SpikeTests", Guid.NewGuid().ToString("N"), "test.sqlite");
    private static SearchListing Item(string id = "12345678", string price = "1 200 000 ₽", string location = "Москва") =>
        SearchParser.Parse(new(false, false, false, false, false, true,
            [new(id, $"https://www.avito.ru/moskva/zemelnye_uchastki/item_{id}", "Участок 10 сот.", price, location, "Сегодня",
                "120 000 ₽ за сотку", "Участок у леса", "Иван", "10 объявлений", "Собственник", "https://00.img.avito.st/image/preview?context=removed")]),
            new("https://www.avito.ru/moskva/zemelnye_uchastki"), new("https://www.avito.ru/moskva/zemelnye_uchastki"), DateTimeOffset.UtcNow).Listings[0];

    [TestMethod]
    public void SearchExtractsEveryLoadedMainCardBeyondTwentyAndPreservesUnitPriceRaw()
    {
        CardSnapshot[] cards = Enumerable.Range(10000000, 25).Select(id => new CardSnapshot(id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            $"https://www.avito.ru/moskva/zemelnye_uchastki/item_{id}", "Участок 10 сот.", "100 000 ₽", "Москва", "Сегодня",
            "10 000 ₽ за сотку", "У леса", "Иван", "10 объявлений", "Собственник")).ToArray();
        SearchParseResult parsed = SearchParser.Parse(new(false, false, false, false, false, true, cards), null, null, DateTimeOffset.UtcNow);
        Assert.AreEqual(25, parsed.Listings.Length);
        Assert.AreEqual("10 000 ₽ за сотку", parsed.Listings[0].PricePerSotka.Raw);
        Assert.AreEqual(10000m, parsed.Listings[0].PricePerSotka.Parsed);
        Assert.AreEqual("У леса", parsed.Listings[0].PreviewDescription);
        Assert.AreEqual("Иван", parsed.Listings[0].SellerName);
        Assert.AreEqual("Собственник", parsed.Listings[0].Badges);
        Assert.AreEqual(25, SpikeJson.Deserialize<SearchParseResult>(SpikeJson.Serialize(parsed)).Listings.Length);
    }

    [TestMethod]
    public void DatabasePersistsHistoryAndDoesNotEraseMissingFields()
    {
        string path = DatabasePath();
        ListingStore store = new(path);
        string run = store.StartRun("https://www.avito.ru/moskva/zemelnye_uchastki?context=removed", 1);
        SearchListing first = Item();
        Assert.AreEqual(120000m, first.PricePerSotka.Parsed);
        Assert.IsFalse(first.PhotoUrl.Contains('?', StringComparison.Ordinal));
        DateTimeOffset time = DateTimeOffset.UtcNow;
        store.Save(run, 1, time, [first]);
        store.Save(run, 1, time.AddMinutes(1), [Item(price: "", location: "") with { SellerName = "" }]);
        ListingStore reopened = new(path);
        SavedListing saved = reopened.ReadListings("иван").Single();
        Assert.AreEqual(1200000m, saved.Price);
        Assert.AreEqual("Москва", saved.Location);
        Assert.AreEqual(time, saved.FirstSeen);
        Assert.AreEqual(time.AddMinutes(1), saved.LastSeen);
        SavedObservation[] history = reopened.ReadHistory(first.ExternalId);
        Assert.AreEqual(2, history.Length);
        Assert.AreEqual(Presence.Absent, history[0].Listing.Price.Presence);
        Assert.AreEqual("", history[0].Listing.Location);
        Assert.IsFalse(reopened.ReadRuns()[0].Url.Contains('?', StringComparison.Ordinal));
        // Parameterized lookups tolerate arbitrary search text without executing SQL.
        Assert.AreEqual(0, reopened.ReadListings("' OR 1=1 --").Length);
    }

    [TestMethod]
    public async Task CollectionLoadsAdditionalCardsAndDeduplicatesAcrossPages()
    {
        ListingStore store = new(DatabasePath());
        FakePage page = new() { Dynamic = true, HasNext = true };
        SearchCollector collector = new(page, store, 2);
        await collector.CollectAsync(null, CancellationToken.None);
        Assert.AreEqual("COMPLETED", collector.State);
        Assert.AreEqual(2, collector.PageNumber);
        Assert.AreEqual(3, collector.Listings.Length);
        Assert.AreEqual(1, page.NextClicks);
        Assert.AreEqual(3, store.ReadListings().Length);
        Assert.AreEqual(2, store.ReadHistory("12345678").Length, "One observation on each page, no duplicates from stable scrolls.");
        Assert.AreEqual(3, store.ReadRuns()[0].Count);
    }

    [TestMethod]
    [DataRow(PageClassification.Captcha)]
    [DataRow(PageClassification.AuthenticationRequired)]
    [DataRow(PageClassification.RateLimited)]
    public async Task ProtectionPausesWithoutClickingAndResumesManually(PageClassification protection)
    {
        ListingStore store = new(DatabasePath());
        FakePage page = new() { Protection = protection, HasNext = true };
        SearchCollector collector = new(page, store, 2);
        await collector.CollectAsync(null, CancellationToken.None);
        Assert.IsTrue(collector.CanResume);
        Assert.AreEqual(protection.ToString(), collector.State);
        Assert.AreEqual(1, collector.Listings.Length);
        Assert.AreEqual(0, page.NextClicks);
        Assert.AreEqual(1, store.ReadListings().Length);
        page.Protection = null;
        await collector.CollectAsync(null, CancellationToken.None);
        Assert.IsFalse(collector.CanResume);
        Assert.AreEqual("COMPLETED", collector.State);
        Assert.AreEqual(2, collector.Listings.Length);
    }

    [TestMethod]
    public async Task CancellationKeepsSavedDataAndInvalidNextNeverClicks()
    {
        ListingStore store = new(DatabasePath());
        using CancellationTokenSource cancellation = new();
        FakePage page = new() { OnScroll = cancellation.Cancel };
        SearchCollector collector = new(page, store, 1);
        await collector.CollectAsync(null, cancellation.Token);
        Assert.AreEqual("STOPPED", collector.State);
        Assert.AreEqual(1, store.ReadListings().Length);
        page = new() { InvalidNext = true, HasNext = true };
        collector = new(page, store, 2);
        await collector.CollectAsync(null, CancellationToken.None);
        Assert.AreEqual("INVALID_NEXT_PAGE", collector.State);
        Assert.AreEqual(0, page.NextClicks);
    }

    [TestMethod]
    public async Task EndlessScrollIsBoundedAndStoppingPauseUnlocksRun()
    {
        ListingStore store = new(DatabasePath());
        FakePage page = new() { NeverBottom = true };
        SearchCollector collector = new(page, store, 1);
        await collector.CollectAsync(null, CancellationToken.None);
        Assert.AreEqual("SCROLL_LIMIT_INCOMPLETE", collector.State);
        Assert.AreEqual(120, page.Scrolls);
        page = new() { Protection = PageClassification.Captcha };
        collector = new(page, store, 1);
        await collector.CollectAsync(null, CancellationToken.None);
        collector.Stop();
        Assert.IsFalse(collector.CanResume);
        Assert.AreEqual("STOPPED", store.ReadRuns()[0].State);
    }

    [TestMethod]
    public async Task HeadedBrowserScrollLoadsDomAndClicksNextWithNoLiveRequests()
    {
        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync(new() { Channel = "chrome", Headless = false, ChromiumSandbox = true });
        await using IBrowserContext context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 900, Height = 600 } });
        const string first = "https://www.avito.ru/moskva/zemelnye_uchastki?p=1";
        const string second = "https://www.avito.ru/moskva/zemelnye_uchastki?p=2";
        await context.RouteAsync("**/*", route => route.FulfillAsync(new()
        {
            ContentType = "text/html",
            Body = route.Request.Url == first ? """
                <h1>Участки</h1><div data-marker="catalog-serp" style="min-height:1100px">
                  <div data-marker="item" data-item-id="12345678"><a data-marker="item-title" href="https://www.avito.ru/moskva/zemelnye_uchastki/item_12345678">Участок 10 сот.</a></div>
                </div><a data-marker="pagination-button/nextPage" href="https://www.avito.ru/moskva/zemelnye_uchastki?p=2">Дальше</a>
                <script>let added=false;addEventListener('scroll',()=>{if(added)return;added=true;setTimeout(()=>{document.querySelector('[data-marker="catalog-serp"]').insertAdjacentHTML('beforeend',`<div data-marker="item" data-item-id="87654321"><a data-marker="item-title" href="https://www.avito.ru/moskva/zemelnye_uchastki/item_87654321">Участок 20 сот.</a></div>`)},300)})</script>
                """ : """
                <h1>Участки</h1><div data-marker="catalog-serp"><div data-marker="item" data-item-id="55555555"><a data-marker="item-title" href="https://www.avito.ru/moskva/zemelnye_uchastki/item_55555555">Участок 30 сот.</a></div></div>
                """
        }));
        IPage target = await context.NewPageAsync();
        await target.GotoAsync(first);
        await using AvitoBrowser adapter = new(target);
        ListingStore store = new(DatabasePath());
        SearchCollector collector = new(adapter, store, 2);
        await collector.CollectAsync(null, CancellationToken.None);
        Assert.AreEqual(second, target.Url);
        Assert.AreEqual("COMPLETED", collector.State);
        Assert.AreEqual(3, collector.Listings.Length);
        Assert.AreEqual(3, store.ReadListings().Length);
    }

    private sealed class FakePage : ISearchPage
    {
        public string CurrentUrl { get; private set; } = "https://www.avito.ru/moskva/zemelnye_uchastki?p=1";
        public int Scrolls { get; private set; }
        public int NextClicks { get; private set; }
        public bool Dynamic { get; init; }
        public bool HasNext { get; init; }
        public bool InvalidNext { get; init; }
        public bool NeverBottom { get; init; }
        public PageClassification? Protection { get; set; }
        public Action? OnScroll { get; init; }

        public Task<SearchParseResult> ReadAsync()
        {
            SearchListing[] cards = NextClicks > 0 ? [Item(), Item("55555555")] :
                Dynamic && Scrolls > 0 ? [Item(), Item("87654321")] : [Item()];
            PageClassification classification = Scrolls > 0 && Protection.HasValue ? Protection.Value : PageClassification.SearchResults;
            ObservationResult metadata = new("1.0", "AVITO", "0.2.1", null, null, DateTimeOffset.UtcNow,
                classification, classification == PageClassification.SearchResults ? Outcome.Success : Outcome.Attention,
                [], [], Guid.NewGuid(), []);
            return Task.FromResult(new SearchParseResult(metadata, classification == PageClassification.SearchResults ? cards : []));
        }

        public Task<bool> ScrollAsync(CancellationToken cancellationToken)
        {
            Scrolls++;
            OnScroll?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(!NeverBottom);
        }
        public Task<string?> NextUrlAsync() => Task.FromResult(HasNext && NextClicks == 0
            ? InvalidNext ? "https://example.test/next" : "https://www.avito.ru/moskva/zemelnye_uchastki?p=2" : null);
        public Task GoNextAsync(string expectedUrl, CancellationToken cancellationToken)
        { NextClicks++; Scrolls = 0; CurrentUrl = expectedUrl; return Task.CompletedTask; }
    }
}
