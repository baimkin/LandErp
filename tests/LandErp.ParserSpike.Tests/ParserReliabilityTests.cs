using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using LandErp.ParserSpike.LocalCollection;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.ParserSpike.Tests;

[TestClass]
public sealed class ParserReliabilityTests
{
    private const string Avito = "https://www.avito.ru/pushkino/zemelnye_uchastki";
    private static string Root() => Path.Combine(Path.GetTempPath(), "LandErp-Reliability", Guid.NewGuid().ToString("N"));
    private static ListingObservation Item(DateTimeOffset time) => new()
    {
        Source = SourceSite.Avito, ExternalId = "12345678", Url = "https://www.avito.ru/pushkino/uchastok_12345678",
        ObservedAtUtc = time, Title = TextValue.Read("Участок"), Price = NumberValue.Read("1000000"),
        AreaSquareMeters = NumberValue.Read("1000"), Location = TextValue.Read("Известный адрес")
    };

    [TestMethod]
    public void WorkspaceMergesMissingFieldsChronologicallyWithoutCrossingModesAndFiltersMergedValues()
    {
        LocalStore store = new(Path.Combine(Root(), "test.sqlite"));
        SearchLink link = store.SaveLink("Local", Avito);
        string batch = store.StartBatch(new(), true, onlyLinkId: link.Id);
        CollectionJob local = store.Claim(batch, SourceSite.Avito, "test")!;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        store.SavePage(local, 1, Avito, [Item(now)], false, null, "first");
        store.SavePage(local, 1, Avito, [Item(now.AddSeconds(2)) with { Price = NumberValue.Read(null), Location = TextValue.Read(null) }], false, null, "missing");
        store.SavePage(local, 1, Avito, [Item(now.AddSeconds(1)) with { Price = NumberValue.Read("2000000") }], false, null, "out of order");
        store.StopBatch(batch);
        SearchLink remote = store.EnsureServerWorkLink("Remote", Avito, SourceSite.Avito);
        CollectionJob server = store.Claim(store.StartBatch(new(), true, onlyLinkId: remote.Id), SourceSite.Avito, "test")!;
        store.SavePage(server, 1, Avito, [Item(now.AddSeconds(3)) with { Price = NumberValue.Read("9000000"), Location = TextValue.Read("Другой режим") }], false, null, "server");
        ListingObservation actual = store.ReadListings(new(ServerWork: false)).Rows.Single().Observation;
        Assert.AreEqual(2000000m, actual.Price.Parsed);
        Assert.AreEqual("Известный адрес", actual.Location.Raw);
        Assert.AreEqual(200000m, actual.DerivedPricePerSotka);
        Assert.AreEqual(9000000m, store.ReadListings(new(ServerWork: true)).Rows.Single().Observation.Price.Parsed);
        Assert.AreEqual(0L, store.ReadListings(new(ServerWork: false, Quality: ListingQualityFilter.MissingPrice)).Total);
        Assert.AreEqual(1L, store.ReadListings(new(Text: "Известный", ServerWork: false)).Total);
        Assert.IsNull(store.History(SourceSite.Avito, "12345678", serverWork: false)[0].Observation.Price.Parsed);
    }

    [TestMethod]
    public void WorkspaceRetainsExplicitAreaConflictAndClearsDerivedPrice()
    {
        LocalStore store = new(Path.Combine(Root(), "test.sqlite")); store.SaveLink("Local", Avito);
        CollectionJob job = store.Claim(store.StartBatch(new()), SourceSite.Avito, "test")!;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        store.SavePage(job, 1, Avito, [Item(now)], false, null, "first");
        store.SavePage(job, 1, Avito, [Item(now.AddSeconds(1)) with { AreaSquareMeters = NumberValue.Read(null), Warnings = ["AREA_CONFLICT"] }], false, null, "conflict");
        ListingObservation actual = store.ReadListings(new(ServerWork: false)).Rows.Single().Observation;
        Assert.IsNull(actual.AreaSquareMeters.Parsed); Assert.IsNull(actual.DerivedPricePerSotka);
    }

    [TestMethod]
    [DataRow("region", "1", "2")]
    [DataRow("maxprice", "1000000", "9000000")]
    [DataRow("land_status[0]", "1", "2")]
    [DataRow("unknown_filter", "a", "b")]
    public void CianRejectsChangedMembershipFilters(string key, string left, string right)
    {
        string first = "https://www.cian.ru/cat.php?deal_type=sale&" + Uri.EscapeDataString(key) + "=" + left;
        Assert.IsFalse(SearchUrls.SameSearch(first, first.Replace("=" + left, "=" + right, StringComparison.Ordinal) + "&p=2", SourceSite.Cian));
    }

    [TestMethod]
    public void DiagnosticsBoundAllJobsAndReleaseCompletedState()
    {
        string root = Root(); LocalStore store = new(Path.Combine(root, "test.sqlite")); store.SaveLink("Local", Avito);
        CollectionJob job = store.Claim(store.StartBatch(new()), SourceSite.Avito, "test")!;
        DiagnosticJournal journal = new(Path.Combine(root, "diagnostics"));
        for (int i = 0; i < 1000; i++) journal.Write(job with { Id = i.ToString(CultureInfo.InvariantCulture) }, 1, "Чтение выдачи", "Выполнено");
        Assert.IsTrue(Cache(journal, "recent").Count <= 32);
        journal.WriteDataQuality(job, 1, Avito, [Item(DateTimeOffset.UtcNow) with { Price = NumberValue.Read(null) }], new Dictionary<string, ListingDataQualityDiagnostic>());
        journal.WriteFailure(job, 1, "Synthetic failure");
        journal.ForgetJob(job.Id);
        Assert.IsFalse(Cache(journal, "recent").Contains(job.Id));
        Assert.IsFalse(Cache(journal, "lastDetailedError").Contains(job.Id));
        Assert.IsFalse(Cache(journal, "dataQualityWritten").Contains(job.Id));
        Assert.IsTrue(File.Exists(journal.ErrorsPath));
    }

    private static IDictionary Cache(DiagnosticJournal journal, string name) => (IDictionary)typeof(DiagnosticJournal)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(journal)!;

    [TestMethod]
    public void DiagnosticDeduplicationIsBoundedWithoutDroppingQualityRecords()
    {
        string root = Root(); LocalStore store = new(Path.Combine(root, "test.sqlite")); store.SaveLink("Local", Avito);
        CollectionJob job = store.Claim(store.StartBatch(new()), SourceSite.Avito, "test")!;
        DiagnosticJournal journal = new(Path.Combine(root, "diagnostics"));
        ListingObservation[] rows = Enumerable.Range(10000000, 2500).Select(id => Item(DateTimeOffset.UtcNow) with
        { ExternalId = id.ToString(CultureInfo.InvariantCulture), Price = NumberValue.Read(null) }).ToArray();
        journal.WriteDataQuality(job, 1, Avito, rows, new Dictionary<string, ListingDataQualityDiagnostic>());
        Assert.IsTrue(((HashSet<string>)Cache(journal, "dataQualityWritten")[job.Id]!).Count <= 2000);
        Assert.AreEqual(2500, File.ReadLines(journal.DataQualityPath).Count());
        journal.ForgetJob(job.Id); Assert.AreEqual(0, Cache(journal, "dataQualityWritten").Count);
    }

    [TestMethod]
    public async Task CancelledPageWriteRollsBackCoverageAndObservations()
    {
        LocalStore store = new(Path.Combine(Root(), "test.sqlite")); store.SaveLink("Local", Avito);
        CollectionJob job = store.Claim(store.StartBatch(new()), SourceSite.Avito, "test")!;
        ListingObservation[] rows = Enumerable.Range(10000000, 5000).Select(id => Item(DateTimeOffset.UtcNow) with
        { ExternalId = id.ToString(CultureInfo.InvariantCulture) }).ToArray();
        using CancellationTokenSource stop = new();
        Task<bool> writing = Task.Run(() => store.SavePage(job, 1, Avito, rows, true, new(NextKind.End), "cancelled", null, null, stop.Token));
        await Task.Delay(20); stop.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () => await writing);
        Assert.AreEqual(0L, store.ReadListings(new()).Total);
        Assert.AreEqual(0, store.Journal(job.Id).Length);
    }

    [TestMethod]
    public async Task StopFencesAnAlreadyRequestedLaunchBeforeItsBackgroundStart()
    {
        LocalStore store = new(Path.Combine(Root(), "test.sqlite")); store.SaveLink("Local", Avito);
        FaultSessions sessions = new(); await using QueueRunner runner = new(store, sessions);
        long requested = runner.StopVersion; runner.Stop();
        await runner.StartAsync(new(), expectedStopVersion: requested);
        Assert.AreEqual(0, sessions.Created);
        Assert.AreEqual(JobState.StoppedInterrupted, store.Jobs().Single().State);
    }

    [TestMethod]
    public async Task StopDoesNotWaitForBusyDatabaseOrOpenBrowserAfterStartingBatch()
    {
        LocalStore store = new(Path.Combine(Root(), "test.sqlite")); store.SaveLink("Local", Avito);
        FaultSessions sessions = new(); await using QueueRunner runner = new(store, sessions);
        using SqliteConnection writer = new(new SqliteConnectionStringBuilder { DataSource = store.Path }.ToString());
        writer.Open(); using SqliteTransaction transaction = writer.BeginTransaction();
        Task run = Task.Run(() => runner.StartAsync(new()));
        try
        {
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(3);
            while (!runner.IsRunning) { Assert.IsTrue(DateTimeOffset.UtcNow < deadline); await Task.Delay(10); }
            Stopwatch watch = Stopwatch.StartNew(); runner.Stop();
            Assert.IsTrue(watch.ElapsedMilliseconds < 250);
        }
        finally { transaction.Commit(); }
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(0, sessions.Created); Assert.AreEqual(JobState.StoppedInterrupted, store.Jobs().Single().State);
    }

    [TestMethod]
    public void TenThousandJobHistoryHasBoundedPagesAndSeparateActiveSnapshot()
    {
        LocalStore store = new(Path.Combine(Root(), "test.sqlite")); store.SaveLink("Local", Avito);
        CollectionJob job = store.Claim(store.StartBatch(new()), SourceSite.Avito, "test")!;
        using (SqliteConnection db = new(new SqliteConnectionStringBuilder { DataSource = store.Path }.ToString()))
        {
            db.Open(); using SqliteCommand command = db.CreateCommand();
            command.CommandText = """
                WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i+1 FROM n WHERE i<10000)
                INSERT INTO local_jobs SELECT 'history-'||i,batch_id,link_id,revision,source,url,'pass-'||i,page,page_limit,
                  'Completed','',NULL,NULL,started FROM n CROSS JOIN local_jobs WHERE id=$id
                """;
            command.Parameters.AddWithValue("$id", job.Id); command.ExecuteNonQuery();
        }
        Stopwatch watch = Stopwatch.StartNew();
        JobPage first = store.ReadJobPage(false); JobPage last = store.ReadJobPage(false, 1000000);
        JobPage active = store.ReadJobPage(false, activeOnly: true);
        Assert.AreEqual(10000L, first.Total); Assert.AreEqual(50, first.Rows.Length);
        Assert.AreEqual(9950, last.Offset); Assert.AreEqual(50, last.Rows.Length);
        Assert.AreEqual(job.Id, active.Rows.Single().Job.Id);
        Console.WriteLine($"History=10000; snapshots=3; rows={first.Rows.Length + last.Rows.Length + active.Rows.Length}; elapsed_ms={watch.ElapsedMilliseconds}");
        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(3), $"Bounded snapshots took {watch.ElapsedMilliseconds} ms");
        Assert.IsNotNull(store.Job("history-1"));
    }

    [TestMethod]
    public async Task JsonFailureIsReportedAndSessionsAreDisposedForBackgroundLaunch()
    {
        LocalStore store = new(Path.Combine(Root(), "test.sqlite")); store.SaveLink("Local", Avito);
        FaultSessions sessions = new(); QueueRunner runner = new(store, sessions);
        await runner.StartAsync(new()).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(JobState.Failed, store.Jobs().Single().State);
        StringAssert.Contains(runner.LastError!, "JsonException");
        Assert.IsFalse(runner.LastError!.Contains("private", StringComparison.Ordinal));
        await runner.DisposeAsync(); Assert.IsTrue(sessions.Disposed);
    }

    private sealed class FaultSessions : ISourceSessions
    {
        public bool Disposed;
        public int Created;
        public Task<ISourcePage> CreatePageAsync(SourceSite source, CollectionSettings settings, CancellationToken cancellationToken)
        { Created++; return Task.FromResult<ISourcePage>(new FaultPage()); }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
    private sealed class FaultPage : ISourcePage
    {
        public SourceSite Source => SourceSite.Avito;
        public string CurrentUrl => Avito;
        public Task OpenAsync(string url, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<PageObservation> ReadAsync(CancellationToken cancellationToken) => Task.FromException<PageObservation>(new System.Text.Json.JsonException("private session payload"));
        public Task<bool> WheelAsync(CollectionSettings settings, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<Pagination> NextAsync(CancellationToken cancellationToken) => Task.FromResult(new Pagination(NextKind.End));
        public Task FollowAsync(Pagination pagination, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ActivateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
