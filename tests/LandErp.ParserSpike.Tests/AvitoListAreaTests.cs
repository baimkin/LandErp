using System.Reflection;
using System.Text.Json;
using LandErp.ParserSpike.LocalCollection;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.ParserSpike.Tests;

[TestClass]
public sealed class AvitoListAreaTests
{
    private const string Url = "https://www.avito.ru/moskva/zemelnye_uchastki?drawId=fixture-zone&f=ASgBAgICAUSq3xUC&metro=2334";
    private static string Root() => Path.Combine(Path.GetTempPath(), "LandErp-ListArea", Guid.NewGuid().ToString("N"));
    private static ListingObservation Item(string id) => new()
    {
        Source = SourceSite.Avito, ExternalId = id, Url = "https://www.avito.ru/moskva/uchastok_" + id,
        ObservedAtUtc = DateTimeOffset.UtcNow, Title = TextValue.Read("Участок"), Price = NumberValue.Read("100")
    };
    [TestMethod]
    public async Task SavedKrekshinoListUsesItsDeclaredAreaWithoutMapNavigationOrCoordinates()
    {
        using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "avito-krekshino-list-state.json")));
        string requested = fixture.RootElement.GetProperty("requestedUrl").GetString()!;
        string state = fixture.RootElement.GetProperty("state").GetRawText();
        int navigations = 0;
        DomCard card = new("12345678", "https://www.avito.ru/moskva/uchastok_12345678", "Участок 10 сот.", "100 ₽", null,
            "Москва", null, null, null, null, null, null, [], []);
        IPage browserPage = DispatchProxy.Create<IPage, BrowserReliabilityTests.InterfaceProxy>();
        ((BrowserReliabilityTests.InterfaceProxy)(object)browserPage).Handler = (name, args) => name switch
        {
            "add_Response" or "remove_Response" => null,
            "get_Url" => requested,
            "GotoAsync" => Navigate(),
            "EvaluateAsync" => Task.FromResult(((string)args![0]!).Contains("const links=", StringComparison.Ordinal)
                ? state : ((string)args[0]!).StartsWith("site =>", StringComparison.Ordinal)
                    ? JsonSerializer.Serialize(new DomSnapshot("SearchResults", [card], [], Layout: "LIST"))
                    : throw new AssertFailedException("No map surface inspection or navigation expected")),
            "CloseAsync" => Task.CompletedTask,
            _ => throw new AssertFailedException("Unexpected page operation: " + name)
        };
        Task<IResponse?> Navigate() { navigations++; return Task.FromResult<IResponse?>(null); }
        await using DomSourcePage adapter = new(browserPage, SourceSite.Avito);
        await adapter.OpenAsync(requested, CancellationToken.None);
        PageObservation result = await adapter.ReadAsync(CancellationToken.None);
        Assert.IsTrue(result.AvitoListSearchConfirmed); Assert.IsNull(result.Map);
        Assert.AreEqual(1, result.Listings.Length); Assert.IsNull(result.Listings[0].Latitude.Parsed);
        Assert.AreEqual(1, navigations);
    }
    [TestMethod]
    public void EverySourceLinkMustConfirmTheSameDrawAndFilters()
    {
        Assert.IsTrue(AvitoSearchIdentity.ConfirmsListLinks(Url, Url, [Url, Url + "&map=e30%3D"], false));
        foreach (string other in new[] { Url.Replace("fixture-zone", "different", StringComparison.Ordinal),
            Url.Replace("drawId=fixture-zone&", "", StringComparison.Ordinal), Url + "&minprice=200",
            Url.Replace("metro=2334", "metro=999", StringComparison.Ordinal), "https://evil.test/map" })
            Assert.IsFalse(AvitoSearchIdentity.ConfirmsListLinks(Url, Url, [Url, other], false), other);
        Assert.IsFalse(AvitoSearchIdentity.ConfirmsListLinks(Url, Url, [], false));
    }
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ListWithDrawIdCollectsTwoPagesWithoutGeometryOrCoordinates(bool server)
    {
        LocalStore store = new(Path.Combine(Root(), "data.sqlite")); store.SaveLink("Крекшино", Url);
        Page page = new("valid"); await using QueueRunner runner = new(store, new Sessions(page));
        await runner.StartAsync(Settings(), serverManaged: server).WaitAsync(TimeSpan.FromSeconds(12));
        CollectionJob job = store.Jobs().Single();
        Assert.AreEqual(CollectionCompletionKind.Success, store.Completion(job.Id)!.Kind, job.Reason);
        Assert.AreEqual(2L, store.ReadListings(new()).Total); Assert.AreEqual(1, page.Follows);
        Assert.IsNull(store.ReadMapScope(job.Id)); Assert.IsNull(runner.SearchAreas.Load(Url, server));
    }
    [TestMethod]
    [DataRow("missing", 0)]
    [DataRow("filters", 0)]
    [DataRow("draw", 0)]
    [DataRow("final-missing", 1)]
    [DataRow("second-page-missing", 1)]
    public async Task ListStopsOnLostSourceProofOrChangedConditionsAndKeepsEarlierData(string scenario, int expected)
    {
        LocalStore store = new(Path.Combine(Root(), "data.sqlite")); store.SaveLink("Крекшино", Url);
        Page page = new(scenario); await using QueueRunner runner = new(store, new Sessions(page));
        await runner.StartAsync(Settings(), serverManaged: true).WaitAsync(TimeSpan.FromSeconds(12));
        CollectionJob job = store.Jobs().Single();
        Assert.AreEqual(CollectionCompletionKind.SourceError, store.Completion(job.Id)!.Kind, job.Reason);
        Assert.AreEqual((long)expected, store.ReadListings(new()).Total);
        if (expected == 0) Assert.AreEqual(0, page.Wheels);
    }
    [TestMethod]
    public async Task MapCannotDowngradeToListConfirmationDuringCollection()
    {
        LocalStore store = new(Path.Combine(Root(), "data.sqlite")); store.SaveLink("Крекшино", Url);
        Page page = new("map-to-list"); await using QueueRunner runner = new(store, new Sessions(page));
        await runner.StartAsync(Settings()).WaitAsync(TimeSpan.FromSeconds(12));
        CollectionJob job = store.Jobs().Single();
        Assert.AreEqual("LAYOUT_CHANGED", store.Completion(job.Id)!.ReasonCode);
        Assert.AreEqual(1L, store.ReadListings(new()).Total);
    }
    private static CollectionSettings Settings() => new() { StabilitySeconds = 1, MaxScrollSteps = 30, LoadWaitSeconds = 1, ScrollPauseMilliseconds = 0 };
    private sealed class Sessions(Page page) : ISourceSessions
    {
        public Task<ISourcePage> CreatePageAsync(SourceSite source, CollectionSettings settings, CancellationToken cancellationToken) => Task.FromResult<ISourcePage>(page);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Page(string scenario) : ISourcePage
    {
        private int number = 1, roundWheels;
        public int Wheels { get; private set; }
        public int Follows { get; private set; }
        public SourceSite Source => SourceSite.Avito;
        public string CurrentUrl => Url + (number > 1 ? "&p=" + number : "")
            + (scenario == "filters" ? "&minprice=200" : "");
        public Task OpenAsync(string url, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<PageObservation> ReadAsync(CancellationToken cancellationToken)
        {
            bool lost = scenario == "missing" || scenario == "final-missing" && roundWheels >= 4
                || scenario == "second-page-missing" && number == 2;
            string source = scenario == "draw" ? CurrentUrl.Replace("fixture-zone", "different", StringComparison.Ordinal) : CurrentUrl;
            bool confirmed = !lost && AvitoSearchIdentity.ConfirmsListLinks(Url, CurrentUrl, [source], false);
            ListingObservation item = Item(number == 1 && !(scenario == "final-missing" && lost) && !(scenario == "map-to-list" && Wheels > 0) ? "12345678" : "12345679");
            MapScope? map = scenario == "map-to-list" && Wheels == 0
                ? new("fixture-zone", true, true, [[new(37,55),new(38,55),new(38,56),new(37,56),new(37,55)]], 1, 1,
                    Selection: new(true, true, true)) : null;
            if (map is not null) item = item with { Latitude = NumberValue.Read("55.5"), Longitude = NumberValue.Read("37.5") };
            return Task.FromResult(new PageObservation(PageKind.SearchResults, [item], [], Map: map, AvitoListSearchConfirmed: confirmed));
        }
        public async Task<bool> WheelAsync(CollectionSettings settings, CancellationToken cancellationToken)
        { Wheels++; roundWheels++; if (roundWheels == 4) await Task.Delay(1100, cancellationToken); return true; }
        public Task<Pagination> NextAsync(CancellationToken cancellationToken) => Task.FromResult(number == 1
            ? new Pagination(NextKind.Next, Url + "&p=2") : new Pagination(NextKind.End));
        public Task FollowAsync(Pagination pagination, CancellationToken cancellationToken)
        { number++; roundWheels = 0; Follows++; return Task.CompletedTask; }
        public Task ActivateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
