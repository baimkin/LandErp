using System.Globalization;
using System.Text;
using System.Text.Json;
using LandErp.ParserSpike.LocalCollection;
using Microsoft.Data.Sqlite;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.ParserSpike.Tests;

[TestClass]
[DoNotParallelize]
public sealed class AvitoMapTests
{
    private const string Url = "https://www.avito.ru/korolev/zemelnye_uchastki?drawId=12345678901234567890123456789012";
    private const string ItemsEndpoint = "https://www.avito.ru/js/1/map/items";
    private const string MarkersEndpoint = "https://www.avito.ru/web/1/map/markers";
    private static readonly GeoPoint[][] Rings = [[new(37,55),new(39,55),new(39,57),new(37,57),new(37,55)]];
    private static string Markers(GeoPoint[][]? rings = null) => JsonSerializer.Serialize(new { drawAreaBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
        type = "Polygon", coordinates = (rings ?? Rings).Select(r => r.Select(p => new[] { p.Longitude, p.Latitude })) }))) });
    private static string Items(int count = 1, decimal price = 100, decimal lat = 56) => JsonSerializer.Serialize(new { totalCount = count, items = new[] { new {
        id = 12345678, urlPath = "/korolev/uchastok_12345678", title = "Участок 10 сот. (ИЖС)", description = "Площадь: 10 соток. Газ.",
        priceDetailed = new { fullString = price.ToString(CultureInfo.InvariantCulture) + " ₽", value = price },
        geo = new { formattedAddress = "Королёв" }, coords = new { lat, lng = "38", precision = 0 },
        images = new[] { new Dictionary<string,string> { ["640x480"] = "https://00.img.avito.st/image/a", ["320x240"] = "https://00.img.avito.st/image/small", ["evil"] = "https://evil.test/picture" } },
        iva = new { DateInfoStep = new[] { new { payload = new { relative = "3 дня назад" } } } }, token = "private-test-secret" } } });
    private static AvitoMapData Data(int count = 1)
    {
        AvitoMapData data = new(); data.Ingest(MarkersEndpoint, Markers(), DateTimeOffset.UtcNow);
        data.Ingest(ItemsEndpoint, Items(count), DateTimeOffset.UtcNow); return data;
    }
    private static LocalStore Store() => new(Path.Combine(Path.GetTempPath(), "LandErp-MapTests", Guid.NewGuid().ToString("N"), "data.sqlite"));

    [TestMethod]
    public void ParsesFieldsCoordinatesAndSafePolygonWithoutSecrets()
    {
        PageObservation page = Data().Read(Url, false); ListingObservation item = page.Listings.Single();
        Assert.AreEqual(1, item.PhotoUrls.Length); Assert.AreEqual("https://00.img.avito.st/image/a", item.PhotoUrls[0]);
        Assert.IsTrue(page.Map!.ZoneConfirmed); Assert.AreEqual(5, page.Map.Rings.Single().Length);
        Assert.AreEqual(56m, item.Latitude.Parsed); Assert.AreEqual(38m, item.Longitude.Parsed);
        Assert.AreEqual(0m, item.CoordinatePrecision.Parsed); Assert.AreEqual(100m, item.Price.Parsed);
        Assert.AreEqual(1000m, item.AreaSquareMeters.Parsed); Assert.AreEqual("Королёв", item.Location.Raw);
        Assert.AreEqual("3 дня назад", item.DateText.Raw); Assert.AreEqual(1, item.PhotoUrls.Length);
        Assert.IsFalse(LocalJson.Write(page).Contains("private-test-secret", StringComparison.Ordinal));
    }
    [TestMethod]
    public void DiagnosticExplainsDuplicatesExclusionsAndDomOnlyCards()
    {
        System.Text.Json.Nodes.JsonObject root=System.Text.Json.Nodes.JsonNode.Parse(Items())!.AsObject();
        System.Text.Json.Nodes.JsonNode original=root["items"]![0]!;
        System.Text.Json.Nodes.JsonArray array=new(original.DeepClone(),original.DeepClone());
        for(int i=1;i<=4;i++)
        {
            System.Text.Json.Nodes.JsonNode item=original.DeepClone();item["id"]=12345678+i;item["urlPath"]="/korolev/uchastok_"+(12345678+i).ToString(CultureInfo.InvariantCulture);
            if(i==1)item["coords"]!["lat"]=58;
            if(i==2)item["coords"]!.AsObject().Remove("lng");
            if(i==3)item["urlPath"]="/korolev/uchastok_99999999";
            if(i==4)item["isSimilarToSearch"]=true;
            array.Add(item);
        }
        root["items"]=array;
        AvitoMapData data=new();data.Ingest(MarkersEndpoint,Markers(),DateTimeOffset.UtcNow);
        data.Ingest(ItemsEndpoint,root.ToJsonString(),DateTimeOffset.UtcNow);
        DomCard card=new("12345683","https://www.avito.ru/korolev/uchastok_12345683","Участок",null,null,null,null,null,null,null,null,null,[],[]);
        data.Enrich(DomSourcePage.Parse(new("SearchResults",[card],[]),SourceSite.Avito,DateTimeOffset.UtcNow).Listings);
        MapDiagnostic diagnostic=data.Read(Url,false).Diagnostic!;MapBatchDiagnostic batch=diagnostic.Batches.Single();
        Assert.AreEqual(6,batch.Received);Assert.AreEqual(5,batch.NewIds);Assert.AreEqual(1,batch.Repeated);
        Assert.AreEqual(1,batch.Recommendations);Assert.AreEqual(1,batch.InvalidIdOrUrl);
        Assert.AreEqual(1,batch.MissingCoordinates);Assert.AreEqual(1,batch.OutsidePolygon);Assert.AreEqual(1,diagnostic.Collected);
        Assert.AreEqual("12345683",diagnostic.DomWithoutJson.Single());
        Assert.AreEqual("12345683",diagnostic.DomNotCollected.Single());
        data.Ingest(ItemsEndpoint,Items(),DateTimeOffset.UtcNow);diagnostic=data.Read(Url,false).Diagnostic!;
        Assert.AreEqual(0,diagnostic.Batches[1].NewIds);Assert.AreEqual(1,diagnostic.Batches[1].Repeated);
        Assert.IsFalse(LocalJson.Write(diagnostic).Contains("private-test-secret",StringComparison.Ordinal));
    }
    [TestMethod]
    public void OutsideOrInvalidCoordinatesAreNotCollectedForDrawnZone()
    {
        AvitoMapData data = Data(); data.Ingest(ItemsEndpoint, Items(lat: 58), DateTimeOffset.UtcNow);
        Assert.AreEqual(0, data.Read(Url,false).Listings.Length);
        data.Ingest(ItemsEndpoint, Items(lat: 91), DateTimeOffset.UtcNow);
        PageObservation page = data.Read(Url,false); Assert.AreEqual(0, page.Listings.Length);
        CollectionAssert.Contains(page.Warnings,"MAP_COORDINATES_MISSING");
    }
    [TestMethod]
    public void MissingChangedAndRestoredZoneRequireConfirmation()
    {
        AvitoMapData data = new(); data.Ingest(ItemsEndpoint, Items(), DateTimeOffset.UtcNow);
        Assert.IsFalse(data.Read(Url,false).Map!.ZoneConfirmed);
        data.Ingest(MarkersEndpoint, Markers(), DateTimeOffset.UtcNow); Assert.IsTrue(data.Read(Url,false).Map!.ZoneConfirmed);
        data.Ingest(MarkersEndpoint, Markers([[new(37,55),new(40,55),new(40,57),new(37,57),new(37,55)]]), DateTimeOffset.UtcNow);
        Assert.IsFalse(data.Read(Url,false).Map!.ZoneConfirmed);
        data.Ingest(MarkersEndpoint, Markers(), DateTimeOffset.UtcNow); Assert.IsTrue(data.Read(Url,false).Map!.ZoneConfirmed);
        data.Ingest(MarkersEndpoint,"{}",DateTimeOffset.UtcNow); Assert.IsFalse(data.Read(Url,false).Map!.ZoneConfirmed);
    }
    [TestMethod]
    public void DegeneratePolygonIsNotConfirmed()
    {
        AvitoMapData data=new();data.Ingest(MarkersEndpoint,Markers([[new(37,55),new(38,56),new(39,57),new(37,55)]]),DateTimeOffset.UtcNow);
        Assert.IsFalse(data.Read(Url,false).Map!.ZoneConfirmed);
    }
    [TestMethod]
    public void DroppedDrawIdCannotBroadenCollectionEvenWithCachedPolygon()
    {
        AvitoMapData data = Data();
        PageObservation page = data.Read("https://www.avito.ru/korolev/zemelnye_uchastki", false, AvitoMapData.DrawId(Url));
        Assert.IsFalse(page.Map!.ZoneConfirmed); Assert.AreEqual(0,page.Listings.Length);
        Assert.IsTrue(data.Read(Url,false,AvitoMapData.DrawId(Url)).Map!.ZoneConfirmed);
    }
    [TestMethod]
    public void GeoJsonHolesAndBoundaryAreHandled()
    {
        GeoPoint[][] rings = [Rings[0], [new(37.5m,55.5m),new(38.5m,55.5m),new(38.5m,56.5m),new(37.5m,56.5m),new(37.5m,55.5m)]];
        Assert.IsTrue(AvitoMapData.Contains(rings,new(37,56)));
        Assert.IsFalse(AvitoMapData.Contains(rings,new(38,56)));
        Assert.IsFalse(AvitoMapData.Contains(rings,new(40,56)));
    }
    [TestMethod]
    public void StoreKeepsChangesIncludingReturnPriceAndAssociatesUnchangedSecondJob()
    {
        LocalStore store=Store(); store.SaveLink("Map",Url); string batch=store.StartBatch(new());
        CollectionJob job=store.Claim(batch,SourceSite.Avito,"one")!;
        AvitoMapData data=Data(); PageObservation page=data.Read(Url,false);
        Save(page); Save(page);
        Assert.AreEqual(1,store.History(SourceSite.Avito,"12345678").Length);
        data.Ingest(ItemsEndpoint,Items(price:200),DateTimeOffset.UtcNow); Save(data.Read(Url,false));
        data.Ingest(ItemsEndpoint,Items(price:100),DateTimeOffset.UtcNow); page=data.Read(Url,false); Save(page);
        Assert.AreEqual(3,store.History(SourceSite.Avito,"12345678").Length);
        Assert.AreEqual(100m,store.ReadListings(new()).Rows.Single().Observation.Price.Parsed);
        Assert.AreEqual(5,store.ReadMapScope(job.Id)!.Rings.Single().Length);
        store.SetState(job,JobState.Completed,"test"); batch=store.StartBatch(new(),force:true);
        job=store.Claim(batch,SourceSite.Avito,"two")!; Save(page);
        Assert.AreEqual(3,store.History(SourceSite.Avito,"12345678").Length);
        Assert.AreEqual(1L,store.ReadListings(new(JobId:job.Id)).Total);
        using MemoryStream export=new();store.ExportJob(job.Id,export);
        using JsonDocument json=JsonDocument.Parse(export.ToArray()); Assert.IsTrue(json.RootElement.TryGetProperty("mapScope",out _));
        void Save(PageObservation snapshot) => Assert.IsTrue(store.SavePage(job,1,Url,snapshot.Listings,false,null,"partial",snapshot.Map,snapshot.Changes));
    }
    [TestMethod]
    public void VersionOneMigrationBacksUpAndPreservesListingsAndHistory()
    {
        LocalStore store=Store(); store.SaveLink("Map",Url); string batch=store.StartBatch(new());CollectionJob job=store.Claim(batch,SourceSite.Avito,"one")!;
        PageObservation page=Data().Read(Url,false); store.SavePage(job,1,Url,page.Listings,false,null,"partial");store.StopBatch(batch);
        using (SqliteConnection db=new(new SqliteConnectionStringBuilder { DataSource=store.Path,Pooling=false }.ToString()))
        // This disposable fixture must contain the v1 schema, without workspace tables added in v3.
        { db.Open();using SqliteCommand command=db.CreateCommand();command.CommandText="DROP TABLE local_link_schedules; DROP TABLE local_groups; DROP TABLE local_map_scopes; DROP TABLE local_sightings; PRAGMA user_version=1;";command.ExecuteNonQuery(); }
        LocalStore migrated=new(store.Path);Assert.IsNotNull(migrated.MigrationBackup);Assert.IsTrue(File.Exists(migrated.MigrationBackup));
        Assert.AreEqual(1L,migrated.ReadListings(new()).Total);Assert.AreEqual(1,migrated.History(SourceSite.Avito,"12345678").Length);
        Assert.AreEqual(1L,migrated.ReadListings(new(JobId:job.Id)).Total);
    }
    private sealed class MapSessions(PageObservation observation, bool bottom = true) : ISourceSessions
    {
        public int Wheels;
        public Task<ISourcePage> CreatePageAsync(SourceSite source,CollectionSettings settings,CancellationToken cancellationToken) => Task.FromResult<ISourcePage>(new MapPage(this,observation,bottom));
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
    private sealed class MapPage(MapSessions owner,PageObservation observation,bool bottom):ISourcePage
    {
        public SourceSite Source=>SourceSite.Avito;
        public string CurrentUrl{get;private set;}=Url;
        public Task OpenAsync(string url,CancellationToken cancellationToken){CurrentUrl=url;return Task.CompletedTask;}
        public Task<PageObservation> ReadAsync(CancellationToken cancellationToken)=>Task.FromResult(observation);
        public Task<bool> WheelAsync(CollectionSettings settings,CancellationToken cancellationToken){owner.Wheels++;return Task.FromResult(bottom);}
        public Task<Pagination> NextAsync(CancellationToken cancellationToken)=>Task.FromResult(new Pagination(NextKind.End));
        public Task FollowAsync(Pagination pagination,CancellationToken cancellationToken)=>throw new InvalidOperationException();
        public Task ActivateAsync(CancellationToken cancellationToken)=>Task.CompletedTask;
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
    [TestMethod]
    public async Task IncompleteMapIsSavedButNeverBecomesFreshCompletedCoverage()
    {
        LocalStore store=Store();store.SaveLink("Map",Url);MapSessions sessions=new(Data(2).Read(Url,false),bottom:false);
        DiagnosticJournal journal=new(Path.Combine(Path.GetDirectoryName(store.Path)!,"diagnostic.jsonl"));
        await using QueueRunner runner=new(store,sessions,journal);
        await runner.StartAsync(new(){MaxPages=10,MaxScrollSteps=20,StabilitySeconds=1,LoadWaitSeconds=2,ScrollPauseMilliseconds=0});
        CollectionJob job=store.Jobs().Single();Assert.AreEqual(JobState.Failed,job.State);
        StringAssert.Contains(job.Reason,"1 / 2");Assert.IsFalse(store.Journal(job.Id).Single().Completed);
        DiagnosticEvent entry=journal.Read().Single(x=>x.Action=="Диагностика карты" && x.Outcome=="Итог");
        MapDiagnostic result=LocalJson.Read<MapDiagnostic>(entry.Detail);Assert.AreEqual(1,result.Collected);Assert.AreEqual(2,result.ExpectedCount);
        Assert.AreEqual(JobState.Pending,store.Jobs(store.StartBatch(new())).Single().State);
    }
    [TestMethod]
    public async Task MapIgnoresPageLimitAndCountHintWhenRealEndIsStable()
    {
        LocalStore store=Store();store.SaveLink("Map",Url);MapSessions sessions=new(Data(2).Read(Url,false));
        await using QueueRunner runner=new(store,sessions);await runner.StartAsync(new(){MaxPages=1,StabilitySeconds=1,LoadWaitSeconds=2,ScrollPauseMilliseconds=100});
        Assert.AreEqual(JobState.Completed,store.Jobs().Single().State);Assert.IsTrue(sessions.Wheels>0);
        Assert.AreEqual(CollectionCompletionKind.Success,store.Completion(store.Jobs().Single().Id)!.Kind);
        Assert.AreEqual(JobState.SkippedFresh,store.Jobs(store.StartBatch(new())).Single().State);
    }
    [TestMethod]
    public async Task MissingZonePausesWithNoticeAndCancellationEndsWaiting()
    {
        LocalStore store=Store();store.SaveLink("Map",Url);
        AvitoMapData data=new();data.Ingest(ItemsEndpoint,Items(),DateTimeOffset.UtcNow);
        await using QueueRunner runner=new(store,new MapSessions(data.Read(Url,false)));
        TaskCompletionSource<CollectionNotice> notice=new(TaskCreationOptions.RunContinuationsAsynchronously);
        runner.ManualActionRequired+=(_,message)=>notice.TrySetResult(message);
        Task run=runner.StartAsync(new(){LoadWaitSeconds=1});
        CollectionNotice shown=await notice.Task.WaitAsync(TimeSpan.FromSeconds(5));
        StringAssert.Contains(shown.Reason,"Карта:");Assert.AreEqual(JobState.AwaitingManualAction,store.Jobs().Single().State);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(()=>runner.ResumeSourceAsync(SourceSite.Avito));
        Assert.AreEqual(0L,store.ReadListings(new()).Total);
        runner.Stop();await run.WaitAsync(TimeSpan.FromSeconds(5));Assert.AreEqual(JobState.StoppedInterrupted,store.Jobs().Single().State);
    }
    [TestMethod]
    public async Task MapWheelScrollsRightContainerCapturesJsonAndDoesNotTouchMap()
    {
        using IPlaywright playwright=await Playwright.CreateAsync();
        await using IBrowser browser=await playwright.Chromium.LaunchAsync(new(){Channel="chrome",Headless=false,ChromiumSandbox=true});
        await using IBrowserContext context=await browser.NewContextAsync();
        await context.RouteAsync("https://www.avito.ru/**",route=>route.FulfillAsync(new(){ ContentType=route.Request.Url.Contains("/map/",StringComparison.Ordinal)?"application/json":"text/html; charset=utf-8",
            Body=route.Request.Url.Contains("/markers",StringComparison.Ordinal)?Markers():route.Request.Url.Contains("/items",StringComparison.Ordinal)?Items():Html }));
        IPage page=await context.NewPageAsync();await using DomSourcePage adapter=new(page,SourceSite.Avito);
        await adapter.OpenAsync(Url,CancellationToken.None);
        await page.WaitForFunctionAsync("window.ready"); await Task.Delay(100);
        PageObservation result=await adapter.ReadAsync(CancellationToken.None);Assert.AreEqual(1,result.Listings.Length);
        Assert.IsTrue(result.Map!.ZoneConfirmed);Assert.AreEqual("Продавец",result.Listings[0].SellerName.Raw);
        await adapter.WheelAsync(new(),CancellationToken.None);await page.WaitForFunctionAsync("document.querySelector('#scroll').scrollTop>0");
        PageObservation after=await adapter.ReadAsync(CancellationToken.None);
        Assert.IsTrue(after.Diagnostic!.ScrollTop>0);Assert.IsTrue(after.Diagnostic.ScrollHeight>after.Diagnostic.ClientHeight);
        Assert.AreEqual(0,await page.EvaluateAsync<int>("window.mapWheels"));Assert.AreEqual(0,await page.EvaluateAsync<int>("window.scrollY"));
        await page.SetContentAsync("<h1>Доступ ограничен</h1><div data-marker='map-full/side-block'></div>");
        Assert.AreEqual(PageKind.Captcha,(await adapter.ReadAsync(CancellationToken.None)).Kind);
    }
    private const string Html="""
        <style>body{margin:0;overflow:hidden}#map{width:50vw;height:95vh;float:left}#side{width:45vw;height:90vh;float:left}#scroll{height:85vh;overflow-y:auto}#list{height:2000px}</style>
        <div id=map>Карта</div><div id=side data-marker="map-full/side-block"><div id=scroll><div id=list data-marker="listItems">
        <div data-marker=""><a data-marker="title" href="/korolev/uchastok_12345678">Участок 10 сот.</a><div class="userInfoStep"><a href="/brands/abc123">Продавец</a><span>44 завершённых объявления</span></div></div>
        </div></div></div><script>window.mapWheels=0;document.querySelector('#map').addEventListener('wheel',()=>window.mapWheels++);
        (async()=>{await (await fetch('/web/1/map/markers')).json();await (await fetch('/js/1/map/items')).json();window.ready=true;})();</script>
        """;
}
