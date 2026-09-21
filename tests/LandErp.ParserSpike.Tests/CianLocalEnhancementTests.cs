using LandErp.ParserSpike.Contracts;
using LandErp.ParserSpike.LocalCollection;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.ParserSpike.Tests;

[TestClass]
[DoNotParallelize]
public sealed class CianLocalEnhancementTests
{
    private static string Database() => Path.Combine(Path.GetTempPath(), "LandErp-CianEnhancement", Guid.NewGuid().ToString("N"), "data.sqlite");
    private const string Polygon = "37.1 55.1,37.2 55.2,37.1 55.1";
    private static string FirstPage =>
        "https://noginsk.cian.ru/cat.php?bbox=55.6%2C37.5%2C55.9%2C38.4&center=55.75%2C37.95&deal_type=sale&engine_version=2&in_polygon%5B0%5D="
        + Uri.EscapeDataString(Polygon) + "&object_type%5B0%5D=3&offer_type=suburban&polygon_name%5B0%5D="
        + Uri.EscapeDataString("Выделенная область") + "&zoom=12";
    private static string SecondPage =>
        "https://www.cian.ru/cat.php?bbox=55.6%2C37.5%2C55.9%2C38.4&deal_type=sale&engine_version=2&in_polygon%5B1%5D="
        + Uri.EscapeDataString(Polygon) + "&object_type%5B0%5D=3&offer_type=suburban&p=2&polygon_name%5B1%5D="
        + Uri.EscapeDataString("Выделенная область");

    [TestMethod]
    public void PolygonPaginationUsesSemanticSearchIdentity()
    {
        Assert.IsTrue(SearchUrls.SameSearch(FirstPage, SecondPage, SourceSite.Cian));
        Assert.AreEqual(2, SearchUrls.PageNumber(SearchUrls.SafePage(SecondPage, SourceSite.Cian)));
        string withoutLabel = string.Join('&', SecondPage.Split('&').Where(x => !x.Contains("polygon_name", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(SearchUrls.SameSearch(FirstPage, withoutLabel, SourceSite.Cian));
        string changedPolygon = SecondPage.Replace(Uri.EscapeDataString(Polygon),
            Uri.EscapeDataString("37.1 55.1,37.25 55.25,37.1 55.1"), StringComparison.Ordinal);
        Assert.IsFalse(SearchUrls.SameSearch(FirstPage, changedPolygon, SourceSite.Cian));
        Assert.IsFalse(SearchUrls.SameSearch(FirstPage, SecondPage.Replace("object_type%5B0%5D=3", "object_type%5B0%5D=2", StringComparison.Ordinal), SourceSite.Cian));
    }

    [TestMethod]
    public void StructuredAndTextLandTypesRemainSeparateAndCanConflict()
    {
        DateTimeOffset now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
        DomCard card = new("334043735", "https://www.cian.ru/sale/suburban/334043735/", "Участок 8 сот. ИЖС",
            "12 000 000 ₽", null, "Балашиха", null, "Участок 8 сот. СНТ.", null, "Продавец", null, null, [], [],
            StructuredArea: "8.0", StructuredAreaUnit: "sotka", StructuredDescription: "Участок 8 сот. СНТ.",
            StructuredPhotos: ["https://images.cdn-cian.ru/images/a.jpg"], CadastralNumber: "50:15:0012345:678",
            SourcePublishedUnix: 1789674155, Latitude: 55.758585m, Longitude: 37.970089m,
            DeclaredLandTypes: ["individualHousingConstruction", "privateFarm"]);

        ListingObservation item = DomSourcePage.Parse(new("SearchResults", [card], []), SourceSite.Cian, now).Listings.Single();

        CollectionAssert.AreEquivalent((LandType[])[LandType.Izhs, LandType.Lph], item.DeclaredLandTypes);
        CollectionAssert.AreEquivalent((LandType[])[LandType.Izhs, LandType.Snt], item.InferredLandTypes);
        Assert.IsTrue(item.LandTypeConflict);
        Assert.AreEqual(800m, item.AreaSquareMeters.Parsed);
        Assert.AreEqual("50:15:0012345:678", item.CadastralNumber.Raw);
        Assert.AreEqual(55.758585m, item.Latitude.Parsed);
        Assert.AreEqual(37.970089m, item.Longitude.Parsed);
        Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(1789674155), item.SourcePublishedAtUtc);
        Assert.AreEqual("DOM+Structured", item.Provenance);
    }

    [TestMethod]
    public async Task PolygonFixtureReadsWhitelistedStructuredFieldsCountHintAndSafePagination()
    {
        string html = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "cian-polygon-structured.html"));
        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync(new() { Channel = "chrome", Headless = false, ChromiumSandbox = true });
        await using IBrowserContext context = await browser.NewContextAsync();
        await context.RouteAsync("**/*", route => route.Request.Url.Contains("/cat.php", StringComparison.Ordinal)
            ? route.FulfillAsync(new() { ContentType = "text/html; charset=utf-8", Body = html }) : route.AbortAsync());
        IPage page = await context.NewPageAsync();
        await using DomSourcePage adapter = new(page, SourceSite.Cian);

        await adapter.OpenAsync(FirstPage, CancellationToken.None);
        PageObservation result = await adapter.ReadAsync(CancellationToken.None);

        Assert.AreEqual(PageKind.SearchResults, result.Kind);
        Assert.AreEqual(2, result.Listings.Length);
        Assert.AreEqual(69, result.SourceCountHint);
        ListingObservation publicAddress = result.Listings.Single(x => x.ExternalId == "334043735");
        Assert.AreEqual(800m, publicAddress.AreaSquareMeters.Parsed);
        Assert.AreEqual(12000000m, publicAddress.Price.Parsed);
        Assert.AreEqual("Россия, Московская область, Балашиха, микрорайон Кучино, Народная улица, 8", publicAddress.Location.Raw);
        Assert.AreEqual(2, publicAddress.PhotoUrls.Length);
        Assert.AreEqual(0, publicAddress.Badges.Length, "Cian badges are intentionally outside the local business contract.");
        Assert.AreEqual(55.758585m, publicAddress.Latitude.Parsed);
        Assert.IsTrue(publicAddress.LandTypeConflict);
        ListingObservation hiddenAddress = result.Listings.Single(x => x.ExternalId == "332776719");
        Assert.AreEqual("Московская область, Ногинск", hiddenAddress.Location.Raw);
        Assert.AreNotEqual("СКРЫТЫЙ ТОЧНЫЙ АДРЕС", hiddenAddress.Location.Raw);
        Pagination next = await adapter.NextAsync(CancellationToken.None);
        Assert.AreEqual(NextKind.Next, next.Kind);
        Assert.AreEqual(2, SearchUrls.PageNumber(next.Url!));
    }

    [TestMethod]
    public void LocalQualityFiltersUseUniversalNormalizedFields()
    {
        LocalStore store = new(Database());
        SearchLink link = store.SaveLink("Cian", "https://www.cian.ru/cat.php?deal_type=sale&offer_type=suburban&object_type%5B0%5D=3");
        string batch = store.StartBatch(new(), force: true, onlyLinkId: link.Id);
        CollectionJob job = store.Claim(batch, SourceSite.Cian, "test")!;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ListingObservation conflict = new()
        {
            Source = SourceSite.Cian, ExternalId = "334043735", Url = "https://www.cian.ru/sale/suburban/334043735/",
            ObservedAtUtc = now, Title = TextValue.Read("ИЖС и СНТ"), Price = NumberValue.Read("12 000 000 ₽"),
            AreaSquareMeters = new(Presence.Present, "8 сот.", 800), CadastralNumber = TextValue.Read("50:15:0012345:678"),
            SourcePublishedAtUtc = now.AddDays(-3), Latitude = NumberValue.Read("55.758585"), Longitude = NumberValue.Read("37.970089"),
            DeclaredLandTypes = [LandType.Izhs], InferredLandTypes = [LandType.Izhs, LandType.Snt]
        };
        ListingObservation missing = conflict with
        {
            ExternalId = "332776719", Url = "https://www.cian.ru/sale/suburban/332776719/", Title = TextValue.Read("Без части данных"),
            CadastralNumber = TextValue.Read(null), SourcePublishedAtUtc = null, Latitude = NumberValue.Read(null), Longitude = NumberValue.Read(null),
            DeclaredLandTypes = [], InferredLandTypes = [], Warnings = ["CHECK_DATA"]
        };
        Assert.IsTrue(store.SavePage(job, 1, link.Url, [conflict, missing], true, new(NextKind.End), "fixture"));

        Assert.AreEqual("334043735", store.ReadListings(new(Quality: ListingQualityFilter.LandTypeConflict)).Rows.Single().ExternalId);
        Assert.AreEqual("332776719", store.ReadListings(new(Quality: ListingQualityFilter.MissingCoordinates)).Rows.Single().ExternalId);
        Assert.AreEqual("332776719", store.ReadListings(new(Quality: ListingQualityFilter.MissingSourcePublishedAt)).Rows.Single().ExternalId);
        Assert.AreEqual("332776719", store.ReadListings(new(Quality: ListingQualityFilter.MissingCadastralNumber)).Rows.Single().ExternalId);
        Assert.AreEqual(2L, store.ReadListings(new(Quality: ListingQualityFilter.Warnings)).Total);
        Assert.AreEqual(1L, store.ReadListings(new("50:15:0012345:678")).Total);
    }
}
