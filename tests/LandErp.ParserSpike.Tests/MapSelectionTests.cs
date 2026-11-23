using LandErp.ParserSpike.LocalCollection;
using LandErp.ParserSpike.ServerIntegration;
using LandErp.Collector.Contracts.V1;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.ParserSpike.Tests;

[TestClass]
public sealed class MapSelectionTests
{
    private const string Url = "https://www.avito.ru/pushkino/zemelnye_uchastki?f=filter";
    private static readonly MapSelectionState Active = new(true, true, true);
    private static string Root() => Path.Combine(Path.GetTempPath(), "LandErp-MapSelection", Guid.NewGuid().ToString("N"));
    private static string Markers(bool active) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", active ? "avito-selection-active.json" : "avito-selection-reset.json"));
    private static ListingObservation Item(string id = "12345678", decimal lat = 55.97m, decimal lon = 37.82m) => new()
    {
        Source = SourceSite.Avito, ExternalId = id, Url = "https://www.avito.ru/pushkino/uchastok_" + id,
        ObservedAtUtc = DateTimeOffset.UtcNow, Title = TextValue.Read("Участок"),
        Latitude = NumberValue.Read(lat.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        Longitude = NumberValue.Read(lon.ToString(System.Globalization.CultureInfo.InvariantCulture))
    };
    private static PageObservation Snapshot(bool geometry = true, MapSelectionState? state = null, ListingObservation[]? items = null)
    {
        AvitoMapData data = new(); data.Ingest("https://www.avito.ru/web/1/map/markers", Markers(geometry), DateTimeOffset.UtcNow);
        PageObservation raw = data.Read(Url, false);
        return raw with { Map = raw.Map! with { Selection = state ?? Active }, Listings = [], UnverifiedListings = items ?? [Item()], Changes = [] };
    }
    [TestMethod]
    public void CapturedResponsesDistinguishSelectedPolygonFromResetWithoutDrawId()
    {
        SearchAreaStore store = new(Root()); SearchAreaGuard guard = new(store, Url, true, "fixture");
        PageObservation active = guard.Apply(Snapshot(items: [Item(), Item("12345679", 57, 39)]), Url);
        Assert.IsTrue(active.AreaCheck!.GeometryMatches);
        Assert.AreEqual(56, active.Map!.ObservedAreas![0][0].Length);
        Assert.AreEqual(1, active.Listings.Length); Assert.AreEqual(1, active.AreaCheck.OutsideCount);
        PageObservation reset = guard.Apply(Snapshot(false, new(true, true, false)), Url);
        Assert.IsFalse(reset.AreaCheck!.GeometryMatches); Assert.IsEmpty(reset.Listings); Assert.IsEmpty(reset.Changes!);
        Assert.IsNotNull(store.Load(Url, true));
    }
    [TestMethod]
    [DataRow(false, true, true)]
    [DataRow(true, false, true)]
    [DataRow(true, true, false)]
    public void HiddenMapOrMissingSurfaceOrResetCannotBootstrapOrUseCachedGeometry(bool map, bool surface, bool reset)
    {
        SearchAreaStore store = new(Root()); SearchAreaGuard guard = new(store, Url, false, "fixture");
        PageObservation blocked = Snapshot(state: new(map, surface, reset));
        Assert.IsEmpty(guard.Apply(blocked, Url).Listings); Assert.IsNull(store.Load(Url, false));
        Assert.AreEqual(1, guard.Apply(Snapshot(), Url).Listings.Length);
        PageObservation result = guard.Apply(blocked, Url);
        Assert.IsFalse(result.Map!.ZoneConfirmed); Assert.IsEmpty(result.Listings);
        Assert.AreEqual("AREA_SELECTION_UNCONFIRMED", result.AreaCheck!.ProblemCode);
    }
    [TestMethod]
    public void UnknownSelectionAndLoadingNeverAuthorizeDataAndFilterChangesStillReject()
    {
        SearchAreaGuard guard = new(new(Root()), Url, false, "fixture");
        PageObservation unknown = Snapshot(); unknown = unknown with { Map = unknown.Map! with { Selection = null } };
        Assert.IsEmpty(guard.Apply(unknown, Url).Listings);
        Assert.IsEmpty(guard.Apply(Snapshot() with { Loading = true }, Url).Listings);
        Assert.AreEqual(1, guard.Apply(Snapshot(), Url).Listings.Length);
        Assert.IsEmpty(guard.Apply(Snapshot(), Url + "&maxprice=200").Listings);
        Assert.AreEqual("AREA_FILTERS_CHANGED", guard.LastCheck!.ProblemCode);
    }
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task RunnerGuardsMapWithoutDrawIdAndDoesNotScrollResetArea(bool selection)
    {
        LocalStore store = new(Path.Combine(Root(), "data.sqlite")); store.SaveLink("Fixture", Url);
        Page page = new(selection ? "valid" : "reset");
        await using QueueRunner runner = new(store, new Sessions(page));
        await runner.StartAsync(new() { MaxScrollSteps = 20, LoadWaitSeconds = 1, StabilitySeconds = 1, ScrollPauseMilliseconds = 100 }).WaitAsync(TimeSpan.FromSeconds(15));
        CollectionJob job = store.Jobs().Single();
        Assert.AreEqual(selection ? 1L : 0L, store.ReadListings(new()).Total, job.Reason);
        Assert.AreEqual(selection ? CollectionCompletionKind.Success : CollectionCompletionKind.Partial, store.Completion(job.Id)!.Kind, job.Reason);
        if (!selection) Assert.AreEqual(0, page.Scrolls);
    }
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ResetDuringScrollPreservesOnlyEarlierVerifiedObservations(bool server)
    {
        LocalStore store = new(Path.Combine(Root(), "data.sqlite")); store.SaveLink("Fixture", Url);
        Page page = new("during-scroll");
        await using QueueRunner runner = new(store, new Sessions(page));
        await runner.StartAsync(new() { MaxScrollSteps = 20, LoadWaitSeconds = 1, StabilitySeconds = 1, ScrollPauseMilliseconds = 100 }, serverManaged: server).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.IsTrue(page.Scrolls > 0); Assert.AreEqual(1L, store.ReadListings(new()).Total);
        CollectionJob job = store.Jobs().Single();
        Assert.AreEqual(CollectionCompletionKind.Partial, store.Completion(job.Id)!.Kind);
        ObservationEnvelope[] observations = store.ReadListings(new()).Rows.Select(row =>
        {
            ListingData data = ServerCoordinator.Map(row.Observation);
            return new ObservationEnvelope(ServerCoordinator.ObservationKey(data), data);
        }).ToArray();
        CollectionResult[] results = CollectionResultClassifier.Build(Guid.NewGuid(), Guid.NewGuid(), job, observations,
            store.Journal(job.Id), store.ReadMapScope(job.Id), store.Completion(job.Id));
        Assert.AreEqual(1, results.Sum(r => r.Observations.Length));
        Assert.AreEqual(CollectionOutcome.Partial, results.Last().Outcome);
    }
    private sealed class Sessions(Page page) : ISourceSessions
    {
        public Task<ISourcePage> CreatePageAsync(SourceSite source, CollectionSettings settings, CancellationToken cancellationToken) => Task.FromResult<ISourcePage>(page);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Page(string scenario) : ISourcePage
    {
        private int reads;
        public int Scrolls { get; private set; }
        public SourceSite Source => SourceSite.Avito;
        public string CurrentUrl => Url;
        public Task OpenAsync(string url, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<PageObservation> ReadAsync(CancellationToken cancellationToken)
        {
            if (scenario == "loading-shell" && ++reads <= 22)
            {
                Assert.AreEqual(0, Scrolls, "Loading map must never scroll");
                return Task.FromResult(Snapshot(state: new(true, false, true, true)) with { Loading = true });
            }
            bool reset = scenario == "reset" || scenario == "during-scroll" && Scrolls > 0;
            return Task.FromResult(Snapshot(!reset, reset ? new(true, true, false) : Active, [Item(reset ? "12345679" : "12345678")]));
        }
        public Task<bool> WheelAsync(CollectionSettings settings, CancellationToken cancellationToken) { Scrolls++; return Task.FromResult(true); }
        public Task<Pagination> NextAsync(CancellationToken cancellationToken) => Task.FromResult(new Pagination(NextKind.End));
        public Task FollowAsync(Pagination pagination, CancellationToken cancellationToken) => throw new AssertFailedException("No map pagination");
        public Task ActivateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [TestMethod]
    public async Task LoadingMapShellWaitsWithoutConsumingScrollBudgetAndThenCollects()
    {
        LocalStore store = new(Path.Combine(Root(), "data.sqlite")); store.SaveLink("Fixture", Url);
        Page page = new("loading-shell");
        await using QueueRunner runner = new(store, new Sessions(page));
        await runner.StartAsync(new() { MaxScrollSteps = 20, LoadWaitSeconds = 1, StabilitySeconds = 1, ScrollPauseMilliseconds = 100 }).WaitAsync(TimeSpan.FromSeconds(20));
        CollectionJob job = store.Jobs().Single();
        Assert.AreEqual(CollectionCompletionKind.Success, store.Completion(job.Id)!.Kind, job.Reason);
        Assert.AreEqual(1L, store.ReadListings(new()).Total);
    }

    [TestMethod]
    public void LoadingMapCannotAuthorizeEvenWithSurfaceResetAndCachedGeometry()
    {
        SearchAreaGuard guard = new(new(Root()), Url, false, "fixture");
        Assert.AreEqual(1, guard.Apply(Snapshot(), Url).Listings.Length);
        Assert.IsEmpty(guard.Apply(Snapshot(state: new(true, true, true, true)), Url).Listings);
    }
}
