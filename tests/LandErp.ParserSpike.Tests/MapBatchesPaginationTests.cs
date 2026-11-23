using System.Reflection;
using System.Text;
using System.Text.Json;
using LandErp.ParserSpike.LocalCollection;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.ParserSpike.Tests;

[TestClass]
public sealed class MapBatchesPaginationTests
{
    private const string Url = "https://www.avito.ru/moskva/zemelnye_uchastki?drawId=fixture-zone";
    private static string Root() => Path.Combine(Path.GetTempPath(), "LandErp-MapBatches", Guid.NewGuid().ToString("N"));
    private static T Proxy<T>(Func<string, object?[]?, object?> handler) where T : class
    {
        T proxy = DispatchProxy.Create<T, BrowserReliabilityTests.InterfaceProxy>();
        ((BrowserReliabilityTests.InterfaceProxy)(object)proxy).Handler = handler; return proxy;
    }
    private static string Items(string id, bool missing = false, bool recommendation = false, bool outside = false) => JsonSerializer.Serialize(new
    {
        totalCount = 144,
        items = new[] { new { id, urlPath = "/moskva/uchastok_" + id, title = "Участок 10 сот.",
            priceDetailed = new { fullString = "100 ₽" }, isSimilarToSearch = recommendation, isOutGeo = outside,
            coords = missing ? null : new { lat = "56", lng = "38" } } }
    });
    private static string Markers() => JsonSerializer.Serialize(new
    {
        drawAreaBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"type":"Polygon","coordinates":[[[37,55],[39,55],[39,57],[37,57],[37,55]]]}"""))
    });
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public async Task ActualCaptureReadsBeyondTenBatchesAndRecoversLateCoordinates(bool outside, bool usePins)
    {
        EventHandler<IResponse>? observer = null; int bodies = 0;
        bool selectionClosed = false;
        DomCard Card(string id) => new(id, "https://www.avito.ru/moskva/uchastok_" + id, "Участок 10 сот.", "100 ₽", null,
            "Москва", null, null, null, null, null, null, [], []);
        IPage page = Proxy<IPage>((name, args) => name switch
        {
            "add_Response" => Add((EventHandler<IResponse>)args![0]!),
            "remove_Response" => Remove((EventHandler<IResponse>)args![0]!),
            "get_Url" => Url,
            "GotoAsync" => Task.FromResult<IResponse?>(null),
            "CloseAsync" => Task.CompletedTask,
            "EvaluateAsync" => selectionClosed ? Task.FromException<string>(new PlaywrightException("closed")) : Task.FromResult(((string)args![0]!).StartsWith("site =>", StringComparison.Ordinal)
                ? JsonSerializer.Serialize(new DomSnapshot("SearchResults", [Card("12345681"), Card("99999999")], [], Layout: "AVITO_MAP"))
                : ((string)args[0]!).Contains("MapVisible:", StringComparison.Ordinal)
                    ? "{\"MapVisible\":true,\"SurfaceVisible\":true,\"ResetVisible\":true}"
                    : "{\"top\":100,\"client\":100,\"height\":200}"),
            _ => throw new AssertFailedException("Unexpected page operation: " + name)
        });
        object? Add(EventHandler<IResponse> h) { observer += h; return null; }
        object? Remove(EventHandler<IResponse> h) { observer -= h; return null; }
        void Response(string endpoint, string json)
        {
            IRequest request = Proxy<IRequest>((name, _) => name switch { "get_IsNavigationRequest" => false, "get_Url" => endpoint + "?drawId=fixture-zone", _ => null });
            IResponse response = Proxy<IResponse>((name, _) => name switch
            {
                "get_Url" => endpoint, "get_Status" => 200, "get_Request" => request,
                "BodyAsync" => Body(), _ => null
            });
            Task<byte[]> Body() { bodies++; return Task.FromResult(Encoding.UTF8.GetBytes(json)); }
            observer!.Invoke(page, response);
        }
        // Legacy page limit intentionally tiny. It must not discard a map response.
        await using DomSourcePage source = new(page, SourceSite.Avito, maximumMapBatches: 1);
        await source.OpenAsync(Url, CancellationToken.None);
        if (usePins)
        {
            using JsonDocument marker = JsonDocument.Parse(Markers());
            string? drawArea = marker.RootElement.GetProperty("drawAreaBase64").GetString();
            Response("https://www.avito.ru/web/1/map/pins", JsonSerializer.Serialize(new { success = new { drawArea } }));
        }
        else Response("https://www.avito.ru/web/1/map/markers", Markers());
        for (int i = 0; i < 10; i++) Response("https://www.avito.ru/js/1/map/items", Items((12345670 + i).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        SearchAreaGuard guard = new(new(Root()), Url, true, "test");
        PageObservation early = guard.Apply(await source.ReadAsync(CancellationToken.None), Url);
        Assert.AreEqual(10, early.Listings.Length); Assert.AreEqual(2, early.AreaCheck!.MissingCoordinates);
        Response("https://www.avito.ru/js/1/map/items", Items("12345680"));
        Response("https://www.avito.ru/js/1/map/items", Items("12345681"));
        Response("https://www.avito.ru/js/1/map/items", Items("99999999", missing: true, recommendation: !outside, outside: outside));
        PageObservation result = guard.Apply(await source.ReadAsync(CancellationToken.None), Url);
        Assert.AreEqual(14, bodies); Assert.AreEqual(13, result.Map!.ResponseBatches);
        Assert.AreEqual(12, result.Listings.Length); Assert.AreEqual(0, result.AreaCheck!.MissingCoordinates);
        Assert.IsNull(result.AreaCheck.ProblemCode); Assert.IsFalse(result.Listings.Any(x => x.ExternalId == "99999999"));
        guard.Persist();
        MapSelectionErrorEvidence captured = (await source.CaptureMapSelectionErrorAsync(CancellationToken.None))!;
        Assert.IsTrue(captured.LastRead!.State.GetProperty("ResetVisible").GetBoolean());
        Assert.IsTrue(captured.AtError!.State.GetProperty("ResetVisible").GetBoolean());
        selectionClosed = true;
        MapSelectionErrorEvidence closed = (await source.CaptureMapSelectionErrorAsync(CancellationToken.None))!;
        Assert.IsNotNull(closed.LastRead);
        Assert.IsNull(closed.AtError);
        Assert.AreEqual("PlaywrightException", closed.CaptureError);
    }
    [TestMethod]
    public void GenuineMissingJsonCoordinatesRemainUnverified()
    {
        AvitoMapData data = new(); data.Ingest("https://www.avito.ru/web/1/map/markers", Markers(), DateTimeOffset.UtcNow);
        data.Ingest("https://www.avito.ru/js/1/map/items", Items("12345678", missing: true), DateTimeOffset.UtcNow);
        PageObservation raw = data.Read(Url, false); raw = raw with { Map = raw.Map! with { Selection = new(true, true, true) } };
        PageObservation result = new SearchAreaGuard(new(Root()), Url, false, "test").Apply(raw, Url);
        Assert.IsEmpty(result.Listings); Assert.AreEqual("AREA_COORDINATES_MISSING", result.AreaCheck!.ProblemCode);
    }
    [TestMethod]
    [DataRow(1, CollectionCompletionKind.LimitReached)]
    [DataRow(2, CollectionCompletionKind.SourceError)]
    public async Task InvalidNextBeyondLimitDoesNotFailVerifiedPageButBeforeLimitStillFails(int limit, CollectionCompletionKind expected)
    {
        string url = "https://www.avito.ru/moskva/zemelnye_uchastki";
        LocalStore store = new(Path.Combine(Root(), "data.sqlite")); store.SaveLink("Архангельское", url);
        PaginationPage page = new(url); await using QueueRunner runner = new(store, new Sessions(page));
        await runner.StartAsync(new() { MaxPages = limit, MaxScrollSteps = 30, StabilitySeconds = 1, ScrollPauseMilliseconds = 0 }).WaitAsync(TimeSpan.FromSeconds(10));
        CollectionJob job = store.Jobs().Single(); Assert.AreEqual(expected, store.Completion(job.Id)!.Kind, job.Reason);
        Assert.AreEqual(1L, store.ReadListings(new()).Total); Assert.IsFalse(store.Completion(job.Id)!.EndReached);
        Assert.AreEqual(expected == CollectionCompletionKind.LimitReached ? "PAGE_LIMIT_REACHED" : "LAYOUT_CHANGED", store.Completion(job.Id)!.ReasonCode);
    }
    private sealed class Sessions(ISourcePage page) : ISourceSessions
    {
        public Task<ISourcePage> CreatePageAsync(SourceSite source, CollectionSettings settings, CancellationToken cancellationToken) => Task.FromResult(page);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class PaginationPage(string url) : ISourcePage
    {
        private int wheels;
        public SourceSite Source => SourceSite.Avito;
        public string CurrentUrl => url;
        public Task OpenAsync(string value, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<PageObservation> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(new PageObservation(PageKind.SearchResults,
            [new() { Source = SourceSite.Avito, ExternalId = "12345678", Url = url + "/uchastok_12345678", Title = TextValue.Read("Участок"), ObservedAtUtc = DateTimeOffset.UtcNow }], []));
        public async Task<bool> WheelAsync(CollectionSettings settings, CancellationToken cancellationToken)
        { if (++wheels == 4) await Task.Delay(1100, cancellationToken); return true; }
        public Task<Pagination> NextAsync(CancellationToken cancellationToken) => Task.FromResult(new Pagination(NextKind.UnknownInvalid, Reason: "Неверный номер следующей страницы"));
        public Task FollowAsync(Pagination pagination, CancellationToken cancellationToken) => throw new AssertFailedException("No skipping pages or navigating beyond limit");
        public Task ActivateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
