using LandErp.ParserSpike.Avito;
using LandErp.ParserSpike.Browser;
using LandErp.ParserSpike.Contracts;
using LandErp.ParserSpike.Serialization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.ParserSpike.Tests;

[TestClass]
public sealed class SearchParserTests
{
    private static readonly Uri SearchUrl = new("https://www.avito.ru/moskva/zemelnye_uchastki?q=test");
    private static SearchSnapshot Snapshot(params CardSnapshot[] cards) => new(false, false, false, false, false, true, cards);
    private static SearchParseResult Parse(SearchSnapshot snapshot) => SearchParser.Parse(snapshot, SearchUrl, SearchUrl, DateTimeOffset.UtcNow);
    private static CardSnapshot Card(string title = "Участок 10,5 сот.", string price = "1 200 000 ₽") =>
        new("12345678", "https://www.avito.ru/moskva/zemelnye_uchastki/uchastok_12345678?context=public", title, price, "Москва", "Сегодня");

    [TestMethod]
    public void SearchTypesFieldsAndExportsWithoutQuery()
    {
        SearchParseResult result = Parse(Snapshot(Card()));
        Assert.AreEqual(PageClassification.SearchResults, result.Metadata.Classification);
        Assert.AreEqual(Outcome.Success, result.Metadata.Outcome);
        Assert.AreEqual(1200000m, result.Listings[0].Price.Parsed);
        Assert.AreEqual(1050m, result.Listings[0].AreaSquareMeters.Parsed);
        Assert.IsFalse(SpikeJson.Serialize(result).Contains("context=", StringComparison.Ordinal));
        Assert.AreEqual("", result.Metadata.RequestedUrl!.Query);
    }

    [TestMethod]
    [DataRow("Участок 2 га", 20000)]
    [DataRow("Участок 1200 м²", 1200)]
    public void AreaUnitsConvertToSquareMeters(string title, int expected)
    {
        Assert.AreEqual((decimal)expected, Parse(Snapshot(Card(title))).Listings[0].AreaSquareMeters.Parsed);
    }

    [TestMethod]
    public void ProtectionAndUnknownNeverLookLikeEmptySuccess()
    {
        SearchSnapshot cards = Snapshot(Card());
        Assert.AreEqual(PageClassification.Captcha, Parse(cards with { Captcha = true }).Metadata.Classification);
        Assert.AreEqual(Outcome.Attention, Parse(cards with { Authentication = true }).Metadata.Outcome);
        Assert.AreEqual(Outcome.Attention, Parse(cards with { RateLimited = true }).Metadata.Outcome);
        Assert.AreEqual(Outcome.Failure, Parse(cards with { SourceError = true }).Metadata.Outcome);
        Assert.AreEqual(0, Parse(cards with { Captcha = true }).Listings.Length);
        Assert.AreEqual(PageClassification.Unknown, Parse(Snapshot()).Metadata.Classification);
        Assert.AreEqual(Outcome.Failure, Parse(Snapshot()).Metadata.Outcome);
    }

    [TestMethod]
    public void MissingIdentityIsRejectedAndMissingFieldsAreVisible()
    {
        Assert.AreEqual(Outcome.Failure, Parse(Snapshot(Card() with { ExternalId = "" })).Metadata.Outcome);
        SearchListing item = Parse(Snapshot(Card("Участок", "по договорённости") with { Location = "", DateText = "" })).Listings[0];
        Assert.AreEqual(Presence.ParseFailed, item.Price.Presence);
        Assert.IsNull(item.Price.Parsed);
        Assert.AreEqual("по договорённости", item.Price.Raw);
        Assert.AreEqual(Presence.Absent, item.AreaSquareMeters.Presence);
        CollectionAssert.Contains(item.Warnings, "LOCATION_ABSENT");
        CollectionAssert.Contains(item.Warnings, "DATE_ABSENT");
    }

    [TestMethod]
    public void NavigationRequiresPublicAvitoHttpsUrl()
    {
        Assert.IsTrue(SearchParser.IsAvitoUrl(SearchUrl));
        Assert.IsFalse(SearchParser.IsAvitoUrl(new Uri("https://avito.ru.example.test/")));
        Assert.IsFalse(SearchParser.IsAvitoUrl(new Uri("http://www.avito.ru/")));
        Assert.IsFalse(SearchParser.IsAvitoUrl(new Uri("https://user:synthetic@www.avito.ru/")));
    }

    [TestMethod]
    public async Task VisibleChromeReadsOnlyMainCardsAndStopsAtOtherRegions()
    {
        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync(new() { Channel = "chrome", Headless = false, ChromiumSandbox = true, Args = ["--enable-automation"] });
        await using IBrowserContext context = await browser.NewContextAsync(new() { ViewportSize = ViewportSize.NoViewport });
        IPage page = await context.NewPageAsync();
        ICDPSession session = await context.NewCDPSessionAsync(page);
        System.Text.Json.JsonElement? commandLine = await session.SendAsync("Browser.getBrowserCommandLine");
        Assert.IsFalse(commandLine!.Value.GetProperty("arguments").EnumerateArray()
            .Any(argument => argument.GetString() == "--no-sandbox"));
        System.Text.Json.JsonElement? windowInfo = await session.SendAsync("Browser.getWindowForTarget");
        int windowId = windowInfo!.Value.GetProperty("windowId").GetInt32();
        await session.SendAsync("Browser.setWindowBounds", new Dictionary<string, object>
        {
            ["windowId"] = windowId,
            ["bounds"] = new { width = 1000, height = 800, windowState = "normal" }
        });
        await page.WaitForFunctionAsync("window.innerWidth <= 1000");
        int smallWidth = await page.EvaluateAsync<int>("window.innerWidth");
        await session.SendAsync("Browser.setWindowBounds", new Dictionary<string, object>
        {
            ["windowId"] = windowId,
            ["bounds"] = new { width = 1300, height = 900, windowState = "normal" }
        });
        await page.WaitForFunctionAsync("previous => window.innerWidth > previous", smallWidth);
        Assert.IsTrue(await page.EvaluateAsync<int>("window.innerWidth") > smallWidth);
        await page.SetContentAsync("""
            <h1>Земельные участки</h1>
            <div data-marker="catalog-serp">
              <div data-marker="item" data-item-id="12345678">
                <a data-marker="item-title" href="https://www.avito.ru/moskva/zemelnye_uchastki/uchastok_12345678">Участок 10 сот.</a>
                <span data-marker="item-price">1 000 000 ₽</span>
                <span data-marker="item-address">Москва</span><span data-marker="item-date">Сегодня</span>
              </div>
              <h2>В других регионах</h2>
              <div data-marker="item" data-item-id="87654321"><a data-marker="item-title">Другой регион</a></div>
            </div>
            <div data-marker="recommendations"><div data-marker="item" data-item-id="99999999">Рекомендация</div></div>
            """);
        SearchSnapshot snapshot = await AvitoBrowser.ReadSnapshotAsync(page);
        Assert.AreEqual(1, snapshot.Cards.Length);
        Assert.AreEqual("12345678", snapshot.Cards[0].ExternalId);
        Assert.AreEqual("Москва", snapshot.Cards[0].Location);
        Assert.AreEqual("1 000 000 ₽", snapshot.Cards[0].Price);
        await page.SetContentAsync("<h1>Доступ ограничен</h1><div data-marker='captcha'>Подтвердите, что вы не робот</div>");
        snapshot = await AvitoBrowser.ReadSnapshotAsync(page);
        Assert.IsTrue(snapshot.Captcha);
        Assert.AreEqual(Outcome.Attention, Parse(snapshot).Metadata.Outcome);
    }
}
