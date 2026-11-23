using System.Text.Json;
using LandErp.Collector.Contracts.V1;
using LandErp.ParserSpike.Desktop;
using LandErp.ParserSpike.LocalCollection;
using LandErp.ParserSpike.ServerIntegration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.ParserSpike.Tests;

[TestClass]
public sealed class SavedSearchAreaTests
{
    private const string Url = "https://www.avito.ru/moskva/zemelnye_uchastki?drawId=original&f=explicit";
    private static string Root() => Path.Combine(Path.GetTempPath(), "LandErp-SavedArea", Guid.NewGuid().ToString("N"));
    private static GeoPoint[] Square(decimal left = 37, decimal bottom = 55) =>
        [new(left, bottom), new(left + 1, bottom), new(left + 1, bottom + 1), new(left, bottom + 1), new(left, bottom)];
    private static GeoPoint[][][] Area => [[Square()]];
    private static ListingObservation Item(string id = "12345678", decimal? lat = 55.5m, decimal? lon = 37.5m) => new()
    {
        Source = SourceSite.Avito, ExternalId = id, Url = "https://www.avito.ru/moskva/uchastok_" + id,
        ObservedAtUtc = DateTimeOffset.UtcNow, Title = TextValue.Read("Участок"), Price = NumberValue.Read("100"),
        Latitude = lat.HasValue ? NumberValue.Read(lat.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)) : NumberValue.Read(null),
        Longitude = lon.HasValue ? NumberValue.Read(lon.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)) : NumberValue.Read(null)
    };
    private static PageObservation Snapshot(GeoPoint[][][]? polygons = null, bool anchor = true, ListingObservation[]? items = null) =>
        new(PageKind.SearchResults, items ?? [Item()], [], Map:
            new("original", true, anchor, (polygons ?? Area)[0], 1, 1, ObservedAreas: polygons ?? Area, Selection: new(true, true, true)));

    [TestMethod]
    public void GeometryIdentityHandlesRotationReversalHolesAndPolygonOrdering()
    {
        GeoPoint[] outer = Square(), hole = Square(37.1m, 55.1m).Select(p => new GeoPoint(37 + (p.Longitude - 37) / 10, 55 + (p.Latitude - 55) / 10)).ToArray();
        GeoPoint[] rotated = [outer[2], outer[3], outer[0], outer[1], outer[2]];
        GeoPoint[][][] first = [[outer, hole], [Square(40, 60)]];
        GeoPoint[][][] second = [[Square(40, 60).Reverse().ToArray()], [rotated.Reverse().ToArray(), hole.Reverse().ToArray()]];
        Assert.AreEqual(SearchAreaGeometry.Signature(first), SearchAreaGeometry.Signature(second));
        Assert.IsTrue(SearchAreaGeometry.Contains(first, Item(lat: 55, lon: 37)));
        Assert.IsFalse(SearchAreaGeometry.Contains(first, Item(lat: 55.05m, lon: 37.05m)));
        Assert.IsTrue(SearchAreaGeometry.Contains(first, Item(lat: 60.5m, lon: 40.5m)));
        Assert.IsFalse(SearchAreaGeometry.Contains(first, Item(lat: 59, lon: 39)));
    }

    [TestMethod]
    public void SmallRoundingIsAllowedButActualGeometryChangeIsNot()
    {
        GeoPoint[][][] rounded = [[Square().Select(p => new GeoPoint(p.Longitude + 0.000000001m, p.Latitude)).ToArray()]];
        Assert.AreEqual(SearchAreaGeometry.Signature(Area), SearchAreaGeometry.Signature(rounded));
        Assert.AreNotEqual(SearchAreaGeometry.Signature(Area), SearchAreaGeometry.Signature([[Square(37.0001m)]]));
        Assert.IsNull(SearchAreaGeometry.Signature([[ [new(37,55),new(38,56),new(37,56),new(38,55),new(37,55)] ]]));
        Assert.IsNull(SearchAreaGeometry.Signature([[Square(190)]]));
        Assert.IsNull(SearchAreaGeometry.Signature([[ [new(37,55),new(38,55),new(37,56)] ]]));
    }

    [TestMethod]
    public void StoreSurvivesRestartIsolatesModesAndNeverReplacesOriginalArea()
    {
        string root = Root(); SearchAreaStore store = new(root);
        SavedSearchArea original = store.Remember(Url, false, Area)!;
        Assert.IsNotNull(original);
        Assert.IsNull(new SearchAreaStore(root).Load(Url, true));
        Assert.IsNull(store.Load(Url + "&maxprice=100", false));
        Assert.AreEqual(original.Signature, new SearchAreaStore(root).Load(Url, false)!.Signature);
        Assert.AreEqual(original.Signature, store.Remember(Url, false, [[Square(40)]])!.Signature);
        Assert.IsNotNull(store.Remember(Url, true, [[Square(40)]]));
        Assert.AreNotEqual(store.Load(Url, false)!.Signature, store.Load(Url, true)!.Signature);
    }

    [TestMethod]
    public void CorruptReferenceIsNotSilentlyOverwritten()
    {
        string root = Root(); Directory.CreateDirectory(root);
        string path = Path.Combine(root, SearchAreaStore.Key(Url, false) + ".json");
        File.WriteAllText(path, "{bad json}");
        Assert.IsNull(new SearchAreaStore(root).Remember(Url, false, Area));
        Assert.AreEqual("{bad json}", File.ReadAllText(path));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SameGeometrySurvivesNewDrawIdButUnverifiedCoordinatesAreQuarantined(bool listMode)
    {
        string root = Root(); SearchAreaStore store = new(root); store.Remember(Url, false, Area);
        SearchAreaGuard guard = new(store, Url, false, "test");
        PageObservation raw = Snapshot(anchor: false, items: [Item(), Item("12345679", 57, 39), Item("12345680", null, null)]);
        if (listMode) raw = raw with { AreaScope = raw.Map, Map = null };
        PageObservation actual = guard.Apply(raw, Url.Replace("original", "replacement", StringComparison.Ordinal) + "&map=changed-viewport");
        Assert.AreEqual(1, actual.Listings.Length); Assert.AreEqual("12345678", actual.Listings[0].ExternalId);
        Assert.IsTrue(actual.AreaCheck!.GeometryMatches); Assert.AreEqual("AREA_COORDINATES_MISSING", actual.AreaCheck.ProblemCode);
        Assert.AreEqual(1, actual.AreaCheck.OutsideCount); Assert.AreEqual(1, actual.AreaCheck.MissingCoordinates);
        guard.Persist();
        string quarantine = File.ReadAllText(Path.Combine(root, SearchAreaStore.Key(Url, false) + "-unverified.json"));
        StringAssert.Contains(quarantine, "12345680"); Assert.IsFalse(quarantine.Contains("12345679", StringComparison.Ordinal));
        Assert.AreEqual(SearchAreaGeometry.Signature(Area), store.Load(Url, false)!.Signature);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void FirstValidGeometryEstablishesReferenceDespiteNewDrawIdWithoutManualConfirmation(bool server, bool listMode)
    {
        SearchAreaStore store = new(Root()); SearchAreaGuard guard = new(store, Url, server, "test");
        string rewritten = Url.Replace("original", "new", StringComparison.Ordinal);
        PageObservation source = Snapshot(anchor: false);
        Assert.IsNull(source.Map!.GeometryRequestDrawId);
        if (listMode) source = source with { Map = null, AreaScope = source.Map };
        PageObservation actual = guard.Apply(source, rewritten);
        Assert.IsTrue(actual.AreaCheck!.GeometryMatches); Assert.AreEqual(1, actual.Listings.Length);
        Assert.IsNull(actual.AreaCheck.ProblemCode);
        Assert.IsNotNull(new SearchAreaStore(store.RootPath).Load(Url, server));
        Assert.IsNull(store.Load(rewritten, server), "The reference belongs to the saved search, not a runtime URL.");
    }

    [TestMethod]
    public void SelectedAreaCanEstablishReferenceWhenRuntimeDrawIdDisappears()
    {
        SearchAreaStore store = new(Root()); SearchAreaGuard guard = new(store, Url, false, "test");
        PageObservation actual = guard.Apply(Snapshot(anchor: false), Url.Replace("drawId=original&", "", StringComparison.Ordinal));
        Assert.IsTrue(actual.AreaCheck!.GeometryMatches); Assert.AreEqual(1, actual.Listings.Length);
        Assert.IsNotNull(store.Load(Url, false));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MissingGeometryCannotEstablishReferenceOrClaimCompleteCoverage(bool hasReference)
    {
        SearchAreaStore store = new(Root());
        if (hasReference) store.Remember(Url, false, Area);
        SearchAreaGuard guard = new(store, Url, false, "test");
        PageObservation missing = Snapshot(anchor: false);
        missing = missing with { Map = missing.Map! with { Rings = [], ObservedAreas = [] } };
        PageObservation actual = guard.Apply(missing, Url.Replace("original", "new", StringComparison.Ordinal));
        Assert.IsFalse(actual.AreaCheck!.GeometryMatches);
        Assert.AreEqual(hasReference ? "AREA_GEOMETRY_UNAVAILABLE" : "AREA_REFERENCE_UNAVAILABLE", actual.AreaCheck.ProblemCode);
        Assert.AreEqual(hasReference, store.Load(Url, false) is not null);
    }

    [TestMethod]
    [DataRow(PageKind.Captcha)]
    [DataRow(PageKind.AuthenticationRequired)]
    [DataRow(PageKind.RateLimited)]
    [DataRow(PageKind.SourceError)]
    [DataRow(PageKind.Unknown)]
    public void NonSearchPagesCannotEstablishReference(PageKind kind)
    {
        SearchAreaStore store = new(Root());
        new SearchAreaGuard(store, Url, false, "test").Apply(Snapshot(anchor: false) with { Kind = kind }, Url);
        Assert.IsNull(store.Load(Url, false));
    }

    [TestMethod]
    [DataRow("&maxprice=200")]
    [DataRow("&unknown=1")]
    [DataRow("&localPriority=1")]
    [DataRow("&drawId=duplicate")]
    public void MatchingPolygonDoesNotAuthorizeChangedFilters(string extra)
    {
        foreach (bool hasReference in new[] { false, true })
        {
            SearchAreaStore store = new(Root());
            if (hasReference) store.Remember(Url, false, Area);
            PageObservation actual = new SearchAreaGuard(store, Url, false, "test").Apply(Snapshot(anchor: false), Url + extra);
            Assert.AreEqual("AREA_FILTERS_CHANGED", actual.AreaCheck!.ProblemCode); Assert.AreEqual(0, actual.Listings.Length);
            Assert.AreEqual(hasReference, store.Load(Url, false) is not null);
        }
    }

    [TestMethod]
    public void ChangedGeometryKeepsReferenceAndMissingGeometryDoesNotClaimSuccess()
    {
        SearchAreaStore store = new(Root()); store.Remember(Url, false, Area);
        SearchAreaGuard guard = new(store, Url, false, "test");
        Assert.AreEqual("AREA_GEOMETRY_CHANGED", guard.Apply(Snapshot([[Square(40)]]), Url).AreaCheck!.ProblemCode);
        PageObservation missing = new(PageKind.SearchResults, [Item()], []);
        PageObservation actual = guard.Apply(missing, Url);
        Assert.AreEqual("AREA_GEOMETRY_UNAVAILABLE", actual.AreaCheck!.ProblemCode);
        Assert.IsEmpty(actual.Listings, "A cached polygon cannot authorize new points without current source geometry.");
        Assert.AreEqual(SearchAreaGeometry.Signature(Area), store.Load(Url, false)!.Signature);
    }

    [TestMethod]
    [DataRow("Polygon")]
    [DataRow("Feature")]
    [DataRow("MultiPolygon")]
    [DataRow("FeatureCollection")]
    public void DecoderUnderstandsSelectedGeoJsonAndNeverUsesViewport(string kind)
    {
        object polygon = new { type = "Polygon", coordinates = Area[0].Select(r => r.Select(p => new[] { p.Longitude, p.Latitude })) };
        object shape = kind switch
        {
            "Feature" => new { type = "Feature", geometry = polygon },
            "FeatureCollection" => new { type = "FeatureCollection", features = new[] { new { type = "Feature", geometry = polygon } } },
            "MultiPolygon" => new { type = "MultiPolygon", coordinates = Area.Select(a => a.Select(r => r.Select(p => new[] { p.Longitude, p.Latitude }))) },
            _ => polygon
        };
        AvitoMapData data = new();
        data.Ingest("https://www.avito.ru/web/1/map/markers", JsonSerializer.Serialize(new { drawArea = shape }), DateTimeOffset.UtcNow);
        Assert.AreEqual(SearchAreaGeometry.Signature(Area), SearchAreaGeometry.Signature(data.Read(Url, false).Map!.ObservedAreas!));
        AvitoMapData viewportOnly = new();
        viewportOnly.Ingest("https://www.avito.ru/web/1/map/markers", "{\"bbox\":[37,55,38,56]}", DateTimeOffset.UtcNow);
        Assert.IsFalse(viewportOnly.Read(Url, false).Map!.ZoneConfirmed);
    }

    [TestMethod]
    public async Task ExplicitBrowserCaptureStoresOnlyExactDisplayedSearch()
    {
        string root = Root(); Sessions sessions = new(new Page("manual"));
        await using WorkspaceController controller = new(Path.Combine(root, "data.sqlite"), root, sessions);
        await controller.OpenManualAsync(new("manual", "Area", Url, SourceSite.Avito, false, false, 1));
        Assert.IsFalse(await controller.CaptureSearchAreaAsync(Url + "&maxprice=200", CancellationToken.None));
        Assert.IsTrue(await controller.CaptureSearchAreaAsync(Url, CancellationToken.None));
        Assert.IsNotNull(new SearchAreaStore(Path.Combine(root, "search-areas")).Load(Url, false));
    }

    [TestMethod]
    public async Task ExistingSearchCanExplicitlyConfirmDisplayedAreaDespiteNewDrawId()
    {
        string root = Root();
        await using WorkspaceController controller = new(Path.Combine(root, "data.sqlite"), root, new Sessions(new Page("valid")));
        await controller.OpenManualAsync(new("manual", "Area", Url, SourceSite.Avito, false, false, 1));
        Assert.IsFalse(await controller.CaptureSearchAreaAsync(Url, CancellationToken.None));
        Assert.IsTrue(await controller.CaptureSearchAreaAsync(Url, CancellationToken.None, explicitlyConfirmVisibleArea: true));
        Assert.IsFalse(await controller.CaptureSearchAreaAsync(Url + "&maxprice=200", CancellationToken.None, explicitlyConfirmVisibleArea: true));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void LoadingSnapshotCannotEstablishPersistentReference(bool rewritten)
    {
        SearchAreaStore store = new(Root()); SearchAreaGuard guard = new(store, Url, false, "test");
        string current = rewritten ? Url.Replace("original", "new", StringComparison.Ordinal) : Url;
        guard.Apply(Snapshot(anchor: false) with { Loading = true }, current);
        Assert.IsNull(store.Load(Url, false));
        guard.Apply(Snapshot(anchor: false), current);
        Assert.IsNotNull(store.Load(Url, false));
    }

    [TestMethod]
    public void ResolvedCoordinatesAreRemovedFromQuarantineAndReferenceIsIndependentOfCallerArrays()
    {
        SearchAreaStore store = new(Root()); GeoPoint[][][] original = Area;
        store.Remember(Url, false, original); SearchAreaGuard guard = new(store, Url, false, "test");
        original[0][0][1] = new(100, 70);
        Assert.AreEqual(SearchAreaGeometry.Signature(Area), store.Load(Url, false)!.Signature);
        Assert.AreEqual("AREA_COORDINATES_MISSING", guard.Apply(Snapshot(items: [Item(lat: null)]), Url).AreaCheck!.ProblemCode);
        Assert.IsNull(guard.Apply(Snapshot(), Url).AreaCheck!.ProblemCode);
    }

    [TestMethod]
    [DataRow("valid", false)]
    [DataRow("missing-coordinates", false)]
    [DataRow("missing-next", true)]
    [DataRow("unavailable", true)]
    [DataRow("changed", true)]
    [DataRow("filters", true)]
    public async Task RunnerPreservesUsefulResultsAndClassifiesCoverage(string scenario, bool server)
    {
        string root = Root(); LocalStore store = new(Path.Combine(root, "data.sqlite")); store.SaveLink("Fixture", Url);
        await using QueueRunner runner = new(store, new Sessions(new Page(scenario)));
        runner.SearchAreas.Remember(Url, server, Area);
        await runner.StartAsync(new() { MaxScrollSteps = 20, StabilitySeconds = 1, LoadWaitSeconds = 2, ScrollPauseMilliseconds = 100 }, serverManaged: server).WaitAsync(TimeSpan.FromSeconds(15));
        CollectionJob job = store.Jobs().Single(); CollectionCompletionFacts facts = store.Completion(job.Id)!;
        CollectionCompletionKind expected = scenario == "valid" ? CollectionCompletionKind.Success
            : scenario == "filters" ? CollectionCompletionKind.SourceError : CollectionCompletionKind.Partial;
        Assert.AreEqual(expected, facts.Kind, job.Reason);
        Assert.AreEqual(scenario == "valid", facts.EndReached); Assert.AreEqual(scenario == "valid", store.Journal(job.Id).Any(p => p.Completed));
        bool verified = scenario is not ("filters" or "changed" or "unavailable");
        Assert.AreEqual(verified ? 1L : 0L, store.ReadListings(new()).Total);
        Assert.AreEqual(SearchAreaGeometry.Signature(Area), runner.SearchAreas.Load(Url, server)!.Signature);
        ObservationEnvelope[] observations = store.ReadListings(new()).Rows.Select(row =>
        {
            ListingData data = ServerCoordinator.Map(row.Observation);
            return new ObservationEnvelope(ServerCoordinator.ObservationKey(data), data);
        }).ToArray();
        Guid remoteJob = Guid.CreateVersion7(), lease = Guid.CreateVersion7();
        CollectionResult[] results = CollectionResultClassifier.Build(remoteJob, lease, job, observations,
            store.Journal(job.Id), store.ReadMapScope(job.Id), facts);
        Assert.AreEqual(Enum.Parse<CollectionOutcome>(expected.ToString()), results.Last().Outcome);
        Assert.IsTrue(results.All(result => result.Observations.Length <= 25 && result.JobId == remoteJob && result.LeaseId == lease));
        Assert.AreEqual(verified ? 1 : 0, results.Sum(result => result.Observations.Length));
        string outboxPath = Path.Combine(root, "test-outbox.sqlite");
        ServerOutbox outbox = new(outboxPath); outbox.EnqueueMany(results);
        CollectionAssert.AreEqual(results.Select(result => result.ResultId).ToArray(),
            new ServerOutbox(outboxPath).Pending().Select(item => item.Result.ResultId).ToArray());
        if (scenario == "missing-coordinates")
        {
            Assert.AreEqual(CollectionCompletionKind.Partial, store.ReadJobPage(false).Rows.Single().CompletionKind);
            string pending = File.ReadAllText(Path.Combine(root, "search-areas", SearchAreaStore.Key(Url, false) + "-unverified.json"));
            StringAssert.Contains(pending, "12345680");
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OldSearchCompletesWithoutManualGateAndPersistsReferenceAcrossRuns(bool server)
    {
        string root = Root(); LocalStore store = new(Path.Combine(root, "data.sqlite")); store.SaveLink("Old search", Url);
        int manualNotices = 0;
        await using QueueRunner runner = new(store, new Sessions(new Page("valid")));
        runner.ManualActionRequired += (_, _) => manualNotices++;
        CollectionSettings settings = new() { StabilitySeconds = 1, MaxScrollSteps = 20 };
        await runner.StartAsync(settings, serverManaged: server).WaitAsync(TimeSpan.FromSeconds(12));
        CollectionJob first = store.Jobs().Single();
        Assert.AreEqual(CollectionCompletionKind.Success, store.Completion(first.Id)!.Kind);
        SavedSearchArea saved = new SearchAreaStore(runner.SearchAreas.RootPath).Load(Url, server)!;
        Assert.IsNotNull(saved); Assert.AreEqual(0, manualNotices);
        await runner.StartAsync(settings, force: true, serverManaged: server).WaitAsync(TimeSpan.FromSeconds(12));
        Assert.AreEqual(2, store.Jobs().Count(job => store.Completion(job.Id)?.Kind == CollectionCompletionKind.Success));
        Assert.AreEqual(saved, new SearchAreaStore(runner.SearchAreas.RootPath).Load(Url, server)! with { Polygons = saved.Polygons });
        Assert.AreEqual(0, manualNotices);
    }

    [TestMethod]
    public async Task ScrollBudgetCannotAdmitPointsWithoutCurrentGeometry()
    {
        string root = Root(); LocalStore store = new(Path.Combine(root, "data.sqlite")); store.SaveLink("Fixture", Url);
        await using QueueRunner runner = new(store, new Sessions(new Page("unavailable")));
        runner.SearchAreas.Remember(Url, true, Area);
        await runner.StartAsync(new() { MaxScrollSteps = 20, LoadWaitSeconds = 30 }, serverManaged: true).WaitAsync(TimeSpan.FromSeconds(12));
        CollectionJob job = store.Jobs().Single();
        Assert.AreEqual(CollectionCompletionKind.Partial, store.Completion(job.Id)!.Kind);
        Assert.AreEqual(0L, store.ReadListings(new()).Total);
        Assert.IsFalse(store.Journal(job.Id).Any(entry => entry.Completed));
    }

    [TestMethod]
    public async Task ManualProtectionResumeUsesSavedAreaInsteadOfBlockingOnRewrittenDrawId()
    {
        string root = Root(); LocalStore store = new(Path.Combine(root, "data.sqlite")); store.SaveLink("Fixture", Url);
        Page page = new("valid") { Captcha = true };
        await using QueueRunner runner = new(store, new Sessions(page));
        runner.SearchAreas.Remember(Url, false, Area);
        TaskCompletionSource notice = new(TaskCreationOptions.RunContinuationsAsynchronously);
        runner.ManualActionRequired += (_, _) => notice.TrySetResult();
        Task run = runner.StartAsync(new() { StabilitySeconds = 1, MaxScrollSteps = 20 });
        await notice.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => runner.ResumeSourceAsync(SourceSite.Avito));
        page.Captcha = false;
        await runner.ResumeSourceAsync(SourceSite.Avito);
        await run.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(CollectionCompletionKind.Success, store.Completion(store.Jobs().Single().Id)!.Kind);
    }

    private sealed class Sessions(Page page) : ISourceSessions
    {
        public Task<ISourcePage> CreatePageAsync(SourceSite source, CollectionSettings settings, CancellationToken cancellationToken) => Task.FromResult<ISourcePage>(page);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Page(string scenario) : ISourcePage
    {
        public bool Captcha { get; set; }
        public SourceSite Source => SourceSite.Avito;
        public string CurrentUrl => (scenario == "manual" ? Url : Url.Replace("original", "replacement", StringComparison.Ordinal)) + (scenario == "filters" ? "&maxprice=200" : "");
        public Task OpenAsync(string url, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<PageObservation> ReadAsync(CancellationToken cancellationToken)
        {
            if (Captcha) return Task.FromResult(new PageObservation(PageKind.Captcha, [], []));
            PageObservation observation = Snapshot(scenario == "changed" ? [[Square(40)]] : Area, false,
                scenario.StartsWith("missing", StringComparison.Ordinal) ? [Item(), Item("12345680", null, null)] : [Item()]);
            if (scenario == "unavailable") observation = observation with { Map = observation.Map! with { Rings = [], ObservedAreas = [] } };
            return Task.FromResult(observation);
        }
        public Task<GeoPoint[][][]?> CaptureSearchAreaAsync(CancellationToken cancellationToken) => Task.FromResult<GeoPoint[][][]?>(Area);
        public Task<bool> WheelAsync(CollectionSettings settings, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<Pagination> NextAsync(CancellationToken cancellationToken) => Task.FromResult(scenario == "missing-next"
            ? new Pagination(NextKind.Next, Url + "&p=2") : new Pagination(NextKind.End));
        public Task FollowAsync(Pagination pagination, CancellationToken cancellationToken) => throw new InvalidOperationException("NO_NAVIGATION_EXPECTED");
        public Task ActivateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
