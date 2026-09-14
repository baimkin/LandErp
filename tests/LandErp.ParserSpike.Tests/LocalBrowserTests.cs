using System.Text.Json;
using LandErp.ParserSpike.LocalCollection;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.ParserSpike.Tests;

[TestClass]
[DoNotParallelize]
public sealed class LocalBrowserTests
{
    [TestMethod]
    public async Task AvitoFullDescriptionSellerStatisticsNumericPaginationAndWheelLoading()
    {
        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync(new() { Channel = "chrome", Headless = false, ChromiumSandbox = true });
        await using IBrowserContext context = await browser.NewContextAsync();
        await context.RouteAsync("https://www.avito.ru/**", route => route.FulfillAsync(new() { ContentType = "text/html; charset=utf-8", Body = AvitoHtml }));
        IPage page = await context.NewPageAsync(); await using DomSourcePage adapter = new(page, SourceSite.Avito);
        await adapter.OpenAsync("https://www.avito.ru/pushkino/zemelnye_uchastki?q=земля", CancellationToken.None);
        PageObservation result = await adapter.ReadAsync(CancellationToken.None);
        Assert.AreEqual(PageKind.SearchResults, result.Kind); Assert.AreEqual(1, result.Listings.Length);
        Assert.IsTrue(result.Listings[0].Description.Raw!.Length > 500);
        StringAssert.Contains(result.Listings[0].Description.Raw!, "Юридическое сопровождение");
        Assert.AreEqual("Исток Недвижимость", result.Listings[0].SellerName.Raw);
        Assert.AreEqual(44m, result.Listings[0].CompletedAdvertisements.Parsed);
        Assert.AreEqual("https://www.avito.ru/brands/abc123", result.Listings[0].SellerUrl.Raw);
        Pagination next = await adapter.NextAsync(CancellationToken.None);
        Assert.AreEqual(NextKind.Next, next.Kind);
        await adapter.WheelAsync(new(), CancellationToken.None);
        await page.WaitForFunctionAsync("window.wheels >= 6");
        Assert.IsTrue(await page.EvaluateAsync<bool>("window.maxDelta <= 120"));
        await adapter.FollowAsync(next, CancellationToken.None);
        Assert.AreEqual(2, SearchUrls.PageNumber(adapter.CurrentUrl));
        await page.SetContentAsync("<div data-marker='catalog-serp'><h1>Выдача</h1></div><div data-marker='pagination'><button disabled>2</button></div>");
        Assert.AreEqual(NextKind.End, (await adapter.NextAsync(CancellationToken.None)).Kind);
        await page.SetContentAsync("<div data-marker='catalog-serp'></div><div data-marker='pagination'><span>...</span></div>");
        Assert.AreEqual(NextKind.UnknownInvalid, (await adapter.NextAsync(CancellationToken.None)).Kind);
        await page.SetContentAsync("<h1>Доступ ограничен</h1><div data-marker='catalog-serp'></div>");
        Assert.AreEqual(PageKind.Captcha, (await adapter.ReadAsync(CancellationToken.None)).Kind);
    }
    [TestMethod]
    public async Task CianMainCardsFullDescriptionAreaConflictAndNoPhoneActions()
    {
        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync(new() { Channel = "chrome", Headless = false, ChromiumSandbox = true });
        await using IBrowserContext context = await browser.NewContextAsync();
        await context.RouteAsync("https://www.cian.ru/**", route => route.FulfillAsync(new() { ContentType = "text/html; charset=utf-8", Body = CianHtml }));
        IPage page = await context.NewPageAsync(); await using DomSourcePage adapter = new(page, SourceSite.Cian);
        await adapter.OpenAsync("https://www.cian.ru/cat.php?region=175744&offer_type=suburban", CancellationToken.None);
        PageObservation result = await adapter.ReadAsync(CancellationToken.None);
        Assert.AreEqual(1, result.Listings.Length); ListingObservation item = result.Listings[0];
        Assert.AreEqual(4850000m, item.Price.Parsed); Assert.AreEqual("Павел Романов", item.SellerName.Raw);
        Assert.IsNull(item.AreaSquareMeters.Parsed); CollectionAssert.Contains(item.Warnings, "AREA_CONFLICT");
        Assert.AreEqual(3, item.Areas.Length); Assert.IsTrue(item.Description.Raw!.Length > 500);
        Assert.AreEqual(2, item.PhotoUrls.Length); Assert.AreEqual("Москва, Пушкино", item.Location.Raw);
        Assert.AreEqual(NextKind.Next, (await adapter.NextAsync(CancellationToken.None)).Kind);
        Assert.AreEqual(0, await page.EvaluateAsync<int>("window.contacts"));
        await page.SetContentAsync("<div data-name='Offers'></div>");
        Assert.AreEqual(NextKind.End, (await adapter.NextAsync(CancellationToken.None)).Kind);
        await page.SetContentAsync("<div class='pagination-new'></div><div data-name='Offers'></div>");
        Assert.AreEqual(NextKind.UnknownInvalid, (await adapter.NextAsync(CancellationToken.None)).Kind);
    }
    [TestMethod]
    public async Task PersistentSessionsHaveSixIndependentLeasesAndSeparateProfiles()
    {
        string root = Path.Combine(Path.GetTempPath(), "LandErp-BrowserSessions", Guid.NewGuid().ToString("N"));
        await using BrowserSessions sessions = new(root);
        List<ISourcePage> pages = [];
        foreach (SourceSite source in Enum.GetValues<SourceSite>())
            for (int i = 0; i < 3; i++) pages.Add(await sessions.CreatePageAsync(source, new(), CancellationToken.None));
        Assert.AreEqual(6, pages.Distinct().Count());
        Assert.AreEqual(3, pages.Count(x => x.Source == SourceSite.Avito));
        Assert.AreEqual(3, pages.Count(x => x.Source == SourceSite.Cian));
        Assert.IsTrue(Directory.Exists(Path.Combine(root, "spike-002", "chrome")));
        Assert.IsTrue(Directory.Exists(Path.Combine(root, "local-cian", "chrome")));
        foreach (ISourcePage page in pages) await page.DisposeAsync();
    }
    [TestMethod]
    public async Task ProvidedCianSnapshotParsesTwentyEightCardsWithScriptsDisabled()
    {
        const string path = "C:/Users/user/Downloads/страница циан.txt";
        if (!File.Exists(path)) { Assert.Inconclusive("Локальный пользовательский образец отсутствует."); return; }
        string html = await File.ReadAllTextAsync(path);
        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync(new() { Channel = "chrome", Headless = false, ChromiumSandbox = true });
        await using IBrowserContext context = await browser.NewContextAsync(new() { JavaScriptEnabled = false });
        await context.RouteAsync("**/*", route => route.Request.Url.Contains("/cat.php", StringComparison.Ordinal)
            ? route.FulfillAsync(new() { ContentType = "text/html; charset=utf-8", Body = html }) : route.AbortAsync());
        IPage page = await context.NewPageAsync(); await using DomSourcePage adapter = new(page, SourceSite.Cian);
        await adapter.OpenAsync("https://www.cian.ru/cat.php?deal_type=sale&engine_version=2&location%5B0%5D=175744&object_type%5B0%5D=3&offer_type=suburban&region=175744", CancellationToken.None);
        PageObservation result = await adapter.ReadAsync(CancellationToken.None);
        Assert.AreEqual(28, result.Listings.Length); Assert.AreEqual(0, result.Warnings.Length);
        ListingObservation first = result.Listings.Single(x => x.ExternalId == "333744054");
        Assert.AreEqual(21900000m, first.Price.Parsed); Assert.AreEqual(2628m, first.AreaSquareMeters.Parsed);
        StringAssert.Contains(first.Transport.Raw!, "19 км от МКАД");
        ListingObservation second = result.Listings.Single(x => x.ExternalId == "333769340");
        Assert.AreEqual("Павел Романов", second.SellerName.Raw); CollectionAssert.Contains(second.Warnings, "AREA_CONFLICT");
        Assert.AreEqual(NextKind.Next, (await adapter.NextAsync(CancellationToken.None)).Kind);
        Console.WriteLine("Provided Cian snapshot: 28 cards; scripts disabled; no external requests allowed.");
    }
    [TestMethod]
    public void InvalidCardsAndProtectionAreNeverSyntheticSuccessfulListings()
    {
        DomCard bad = new("12345678", "https://www.cian.ru/sale/suburban/99999999/", "Title", "123 ₽", null, null, null, null, null, null, null, null, [], []);
        Assert.AreEqual(0, DomSourcePage.Parse(new("Captcha", [bad], []), SourceSite.Cian, DateTimeOffset.UtcNow).Listings.Length);
        PageObservation invalid = DomSourcePage.Parse(new("SearchResults", [bad], []), SourceSite.Cian, DateTimeOffset.UtcNow);
        CollectionAssert.Contains(invalid.Warnings, "INVALID_ID_OR_URL");
    }
    private static string LongDescription => string.Join("\n\n", Enumerable.Repeat("Юридическое сопровождение. Все данные из DOM, без усечения строки.", 20));
    private static string AvitoHtml => $$"""
        <h1>Земельные участки</h1><div data-marker="catalog-serp">
        <div data-marker="item" data-item-id="12345678">
        <a data-marker="item-title" href="/pushkino/zemelnye_uchastki/uchastok_12345678">Участок 9,6 сот. (ИЖС)</a>
        <span data-marker="item-price">8 960 000 ₽</span><p data-marker="item-address">Пушкино</p>
        <div class="bottomBlock-new"><div class="ivaItemRedesign-new"><p style="--module-max-lines-size:4">{{LongDescription}}</p></div></div>
        <div class="userInfoStep-new"><a href="/brands/abc123?src=search_seller_info"><p>Исток Недвижимость</p></a><span><p>44 завершённых объявления</p></span></div>
        <span data-marker="badge-title-2360">Реквизиты проверены</span><p data-marker="item-date">3 дня назад</p>
        </div><h2>В других регионах</h2><div data-marker="item"><a data-marker="item-title" href="/item_87654321">Иное</a></div></div>
        <div style="height:2500px"></div><nav data-marker="pagination"><button disabled>1</button><a href="?q=земля&p=2">2</a></nav>
        <script>window.wheels=0;window.maxDelta=0;addEventListener('wheel',e=>{wheels++;maxDelta=Math.max(maxDelta,e.deltaY);});</script>
        """;
    private static string CianHtml => $$"""
        <h1>Продажа участков</h1><div data-name="Offers">
        <article data-name="CardComponent"><a href="/sale/suburban/333769340/"><div data-name="TitleComponent">Участок, 760 м², 7 сот., ИЖС</div></a>
        <div data-name="GeneralInfoSectionRowComponent"><span>4 850 000 ₽</span></div>
        <div data-name="GeoLabel">Москва</div><div data-name="GeoLabel">Пушкино</div>
        <div data-name="Description">Участок 7.6 сот. {{LongDescription}}</div>
        <div data-name="TimeLabel">8 часов назад сегодня, 11:25</div><a href="/agents/13993860/">Павел Романов</a>
        <div data-name="FeatureLabels">Суперагент</div><div data-name="Gallery"><img src="https://images.cdn-cian.ru/images/1.jpg"><img src="https://images.cdn-cian.ru/images/2.jpg"></div>
        <button data-name="PhoneButton" onclick="contacts++">Показать телефон</button></article></div>
        <nav data-name="Pagination"><button disabled>1</button><a href="?region=175744&offer_type=suburban&p=2">2</a></nav><script>window.contacts=0;</script>
        """;
}
