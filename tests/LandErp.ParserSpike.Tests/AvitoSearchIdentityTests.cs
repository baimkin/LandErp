using LandErp.ParserSpike.LocalCollection;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.ParserSpike.Tests;

[TestClass]
[DoNotParallelize]
public sealed class AvitoSearchIdentityTests
{
    private const string Requested = "https://www.avito.ru/moskovskaya_oblast/zemelnye_uchastki?drawId=fixture-zone&localPriority=0&map=e30%3D";
    private const string Canonical = "https://www.avito.ru/moskovskaya_oblast/zemelnye_uchastki?drawId=fixture-zone&f=ASgBAgICAUSq3xUC&map=e30%3D";
    private static readonly GeoPoint[][] Rings = [[new(37, 55), new(39, 55), new(39, 57), new(37, 57), new(37, 55)]];
    private static MapScope Scope => new("fixture-zone", true, true, Rings, 1, 1);

    [TestMethod]
    public void NeutralAdditionRequiresBothFormAndPolygonProof()
    {
        Assert.IsFalse(SearchUrls.SameSearch(Requested, Canonical, SourceSite.Avito));
        Assert.IsTrue(AvitoSearchIdentity.ConfirmsList(Requested, Canonical, Canonical, true));
        Assert.IsTrue(AvitoSearchIdentity.Confirmed(Requested, Canonical, Scope, true));
        Assert.IsFalse(AvitoSearchIdentity.Confirmed(Requested, Canonical, Scope, false));
        Assert.IsFalse(AvitoSearchIdentity.Confirmed(Requested, Canonical, null, true));
        Assert.IsFalse(AvitoSearchIdentity.Confirmed(Requested, Canonical, Scope with { Rings = [] }, true));
        Assert.IsFalse(AvitoSearchIdentity.Confirmed(Requested, Canonical, Scope with { ZoneConfirmed = false }, true));
        Assert.IsFalse(AvitoSearchIdentity.Confirmed(Requested, Canonical, Scope with { DrawId = "other" }, true));
    }

    [TestMethod]
    [DataRow("&minprice=100")]
    [DataRow("&maxprice=200")]
    [DataRow("&q=house")]
    [DataRow("&unknown=1")]
    [DataRow("&localPriority=1")]
    [DataRow("&deal_type=rent")]
    public void ChangedMembershipFiltersAreRejectedEvenWithPolygon(string suffix)
    {
        Assert.IsFalse(AvitoSearchIdentity.Confirmed(Requested, Canonical + suffix, Scope, true));
        Assert.IsFalse(AvitoSearchIdentity.ConfirmsList(Requested, Canonical, Canonical + suffix, true));
    }

    [TestMethod]
    public void ZonePathAndOpaqueFiltersCannotBeReplacedOrDropped()
    {
        foreach (string target in new[] {
            Canonical.Replace("fixture-zone", "other-zone", StringComparison.Ordinal),
            Canonical.Replace("drawId=fixture-zone&", "", StringComparison.Ordinal),
            Canonical.Replace("ASgBAgICAUSq3xUC", "opaque-other", StringComparison.Ordinal),
            Canonical.Replace("moskovskaya_oblast", "tula", StringComparison.Ordinal),
            Canonical.Replace("zemelnye_uchastki", "zemelnye_uchastki/prodam-ASgBAgICAUSWA9oQ", StringComparison.Ordinal) })
            Assert.IsFalse(AvitoSearchIdentity.Confirmed(Requested, target, Scope, true), target);
        Assert.IsFalse(AvitoSearchIdentity.Confirmed(Requested + "&f=explicit-filter", Canonical, Scope, true));
        Assert.IsTrue(AvitoSearchIdentity.Confirmed(Requested + "&f=explicit-filter&minprice=100",
            Requested + "&f=explicit-filter&minprice=100&map=viewport", Scope, false));
        Assert.IsFalse(AvitoSearchIdentity.Confirmed(Requested + "&unknown=1", Canonical, Scope, true));
        Assert.IsFalse(AvitoSearchIdentity.ConfirmsList(Requested, Canonical, "https://evil.test/map", true));
        Assert.IsFalse(AvitoSearchIdentity.Confirmed(Requested, Canonical + "&drawId=other", Scope, true));
    }

    private static PageObservation Observation => new(PageKind.SearchResults, [new()
    {
        Source = SourceSite.Avito, ExternalId = "12345678", Url = "https://www.avito.ru/korolev/uchastok_12345678",
        ObservedAtUtc = DateTimeOffset.UtcNow, Title = TextValue.Read("Участок 10 сот."), Price = NumberValue.Read("100"),
        Latitude = NumberValue.Read("56"), Longitude = NumberValue.Read("38")
    }], [], Map: Scope with { Selection = new(true, true, true) }, AvitoNeutralFormConfirmed: true);

    [TestMethod]
    [DataRow(false, false, false)]
    [DataRow(true, false, false)]
    [DataRow(false, true, false)]
    [DataRow(false, false, true)]
    [DataRow(true, false, true)]
    [DataRow(false, true, true)]
    public async Task RunnerRejectsChangedFiltersBeforeSavingOrOnFinalRead(bool changedInitially, bool changedOnFinal, bool listMode)
    {
        LocalStore store = new(Path.Combine(Path.GetTempPath(), "LandErp-Identity", Guid.NewGuid().ToString("N"), "data.sqlite"));
        store.SaveLink("Fixture", Requested);
        await using QueueRunner runner = new(store, new IdentitySessions(changedInitially, changedOnFinal, listMode));
        await runner.StartAsync(new() { MaxScrollSteps = 30, StabilitySeconds = 1, ScrollPauseMilliseconds = 100 }, serverManaged: true);
        CollectionJob job = store.Jobs().Single();
        Assert.AreEqual(changedInitially || changedOnFinal ? JobState.Failed : JobState.Completed, job.State);
        Assert.AreEqual(changedInitially ? 0L : 1L, store.ReadListings(new()).Total);
        Assert.AreEqual(!changedInitially && !changedOnFinal, store.Journal(job.Id).Any(p => p.Completed));
    }

    private sealed class IdentitySessions(bool initiallyChanged, bool finalChanged, bool listMode) : ISourceSessions
    {
        public Task<ISourcePage> CreatePageAsync(SourceSite source, CollectionSettings settings, CancellationToken cancellationToken)
            => Task.FromResult<ISourcePage>(new IdentityPage(initiallyChanged, finalChanged, listMode));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class IdentityPage(bool initiallyChanged, bool finalChanged, bool listMode) : ISourcePage
    {
        private int wheels;
        public SourceSite Source => SourceSite.Avito;
        public string CurrentUrl { get; private set; } = Canonical;
        public Task OpenAsync(string url, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<PageObservation> ReadAsync(CancellationToken cancellationToken)
        {
            CurrentUrl = Canonical + (initiallyChanged || finalChanged && wheels >= 4 ? "&minprice=200" : "");
            return Task.FromResult(listMode ? Observation with { Map = null, AreaScope = Scope, AvitoListSearchConfirmed = true } : Observation);
        }
        public async Task<bool> WheelAsync(CollectionSettings settings, CancellationToken cancellationToken)
        {
            // The fourth stable snapshot satisfies the dwell time; its successor
            // is the control read, independently of the test machine's speed.
            wheels++; await Task.Delay(wheels == 4 ? 1100 : 10, cancellationToken); return true;
        }
        public Task<Pagination> NextAsync(CancellationToken cancellationToken) => Task.FromResult(new Pagination(NextKind.End));
        public Task FollowAsync(Pagination pagination, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task ActivateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [TestMethod]
    [DataRow("ok")]
    [DataRow("price")]
    [DataRow("missing-zone")]
    [DataRow("changed-zone")]
    [DataRow("foreign-link")]
    [DataRow("deal")]
    [DataRow("seller")]
    [DataRow("checked-photo")]
    [DataRow("unknown-control")]
    [DataRow("changed-filter")]
    [DataRow("hidden-control")]
    [DataRow("unknown-select")]
    [DataRow("mismatched-choice")]
    [DataRow("native-choice")]
    [DataRow("pagination")]
    public async Task BrowserParsesSavedListWithoutOpeningMap(string scenario)
    {
        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync(new() { Channel = "chrome", Headless = true, ChromiumSandbox = true });
        await using IBrowserContext context = await browser.NewContextAsync();
        await context.RouteAsync("**/*", route => route.AbortAsync());
        int mapRequests = 0, navigations = 0;
        await context.RouteAsync("https://www.avito.ru/**", route =>
        {
            if (new Uri(route.Request.Url).AbsolutePath.Contains("/map/", StringComparison.Ordinal))
            { Interlocked.Increment(ref mapRequests); return route.AbortAsync(); }
            if (route.Request.IsNavigationRequest) Interlocked.Increment(ref navigations);
            return route.FulfillAsync(new() { ContentType = "text/html; charset=utf-8",
                Body = ListHtml(scenario, SearchUrls.PageNumber(route.Request.Url)) });
        });
        IPage page = await context.NewPageAsync();
        int mapClicks = 0;
        page.Console += (_, message) => { if (message.Text == "fixture-map-click") Interlocked.Increment(ref mapClicks); };
        await using DomSourcePage adapter = new(page, SourceSite.Avito);
        await adapter.OpenAsync(Requested, CancellationToken.None);
        PageObservation initial = await adapter.ReadAsync(CancellationToken.None);
        Assert.IsNull(initial.Map, "List parsing must never claim a confirmed map polygon.");
        Assert.AreEqual(1, navigations); Assert.AreEqual(0, mapRequests);
        Assert.IsFalse(await page.EvaluateAsync<bool>("!!window.clicked"));
        if (scenario is "price" or "foreign-link" or "deal" or "seller" or "checked-photo" or "unknown-control" or "changed-filter" or "unknown-select" or "mismatched-choice" or "missing-zone" or "changed-zone")
        {
            Assert.IsFalse(initial.AvitoListSearchConfirmed); Assert.AreEqual(0, initial.Listings.Length);
            Assert.IsNotNull(initial.SearchIdentityError); return;
        }
        Assert.IsTrue(initial.AvitoListSearchConfirmed); Assert.AreEqual(1, initial.Listings.Length);
        Assert.AreEqual(100m, initial.Listings.Single().Price.Parsed);
        Assert.AreEqual(scenario == "pagination" ? NextKind.Next : NextKind.End, (await adapter.NextAsync(CancellationToken.None)).Kind);
        LocalStore store = new(Path.Combine(Path.GetTempPath(), "LandErp-IdentityBrowser", Guid.NewGuid().ToString("N"), "data.sqlite"));
        store.SaveLink("Fixture", Requested);
        await using QueueRunner runner = new(store, new SinglePageSessions(adapter));
        await runner.StartAsync(new() { MaxPages = 3, PageIntervalSeconds = 1, StabilitySeconds = 1, MaxScrollSteps = 30, ScrollPauseMilliseconds = 100, LoadWaitSeconds = 2 }, serverManaged: true);
        CollectionJob job = store.Jobs().Single();
        Assert.AreEqual(CollectionCompletionKind.Partial, store.Completion(job.Id)!.Kind, job.Reason);
        Assert.AreEqual(0L, store.ReadListings(new()).Total, "The fixture exposes no coordinates or independently confirmed geometry.");
        Assert.IsNull(store.ReadMapScope(job.Id));
        Assert.IsFalse(store.Journal(job.Id).Any(entry => entry.Completed));
        Assert.AreEqual(2, navigations);
        Assert.AreEqual(0, mapRequests);
        Assert.AreEqual(0, mapClicks);
    }

    private sealed class SinglePageSessions(ISourcePage page) : ISourceSessions
    {
        public Task<ISourcePage> CreatePageAsync(SourceSite source, CollectionSettings settings, CancellationToken cancellationToken)
            => Task.FromResult(page);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static string ListHtml(string scenario, int pageNumber)
    {
        string url = scenario == "foreign-link" ? "https://evil.test/map"
            : scenario == "changed-filter" ? Canonical.Replace("ASgBAgICAUSq3xUC", "other-filter", StringComparison.Ordinal) : Canonical;
        if (scenario == "missing-zone") url = url.Replace("drawId=fixture-zone&", "", StringComparison.Ordinal);
        string current = scenario == "changed-zone" ? Canonical.Replace("fixture-zone", "other-zone", StringComparison.Ordinal) : Canonical;
        if (pageNumber > 1) current += "&p=" + pageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return """
            <div data-marker="catalog-serp"><h1>Участки</h1><div data-marker="item" data-item-id="ITEMID">
            <a data-marker="item-title" href="/korolev/uchastok_ITEMID">Участок 10 сот.</a>
            <p data-marker="item-price">100 ₽</p></div></div>PAGINATION
            <div data-marker="search-filters"><div><span data-marker="categoryId/select-text">Земельные участки</span>
            <select tabindex="-1" data-marker="categoryId/select"></select></div>
            <div><span data-marker="params[203]/select-text">DEAL</span>
            <select tabindex="-1" data-marker="params[203]/select">OPTIONS</select></div>
            <div data-marker="geoFilters/select">Московская область</div>
            <input data-marker="price-from/input" value="PRICE"><input data-marker="price-to/input">
            <input data-marker="params[149569]/input"><input type="hidden" name="params[156912]">
            <label><input type="radio" checked>SELLER</label><input type="checkbox" name="withImagesOnly" PHOTO>UNKNOWN</div>
            HIDDEN<a data-marker="map-button" href="MAPURL">Показать на карте</a>
            <script>history.replaceState(null,'','CURRENT');
            [...document.querySelectorAll('[data-marker="map-button"]')].forEach(a=>a.onclick=e=>{e.preventDefault();window.clicked=true;console.info('fixture-map-click');});</script>
            """.Replace("PRICE", scenario == "price" ? "500" : "", StringComparison.Ordinal)
            .Replace("DEAL", scenario == "deal" ? "Купить" : "Купить или снять", StringComparison.Ordinal)
            .Replace("OPTIONS", scenario == "mismatched-choice" ? "<option selected>Купить</option>"
                : scenario == "native-choice" ? "<option selected>Купить или снять</option>" : "", StringComparison.Ordinal)
            .Replace("SELLER", scenario == "seller" ? "Частные" : "Все", StringComparison.Ordinal)
            .Replace("PHOTO", scenario == "checked-photo" ? "checked" : "", StringComparison.Ordinal)
            .Replace("UNKNOWN", scenario == "unknown-control" ? "<input name='unknown' value='1'>"
                : scenario == "unknown-select" ? "<div data-marker='new-filter/select'>Выбрано</div>" : "", StringComparison.Ordinal)
            .Replace("HIDDEN", scenario == "hidden-control" ? "<a data-marker='map-button' style='display:none' href='https://evil.test/map'>Скрыто</a>" : "", StringComparison.Ordinal)
            .Replace("ITEMID", pageNumber == 1 ? "12345678" : "12345679", StringComparison.Ordinal)
            .Replace("PAGINATION", scenario == "pagination" ? pageNumber == 1
                ? "<div data-marker='pagination'><a href='" + Canonical + "&p=2'>2</a></div>"
                : "<div data-marker='pagination'><span>2</span></div>" : "", StringComparison.Ordinal)
            .Replace("MAPURL", url, StringComparison.Ordinal).Replace("CURRENT", current, StringComparison.Ordinal);
    }
}
