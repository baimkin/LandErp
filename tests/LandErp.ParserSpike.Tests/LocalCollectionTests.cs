using System.Diagnostics;
using LandErp.ParserSpike.Avito;
using LandErp.ParserSpike.Contracts;
using LandErp.ParserSpike.LocalCollection;
using LandErp.ParserSpike.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.ParserSpike.Tests;

[TestClass]
public sealed class LocalCollectionTests
{
    private static string Database() => Path.Combine(Path.GetTempPath(), "LandErp-LocalTests", Guid.NewGuid().ToString("N"), "data.sqlite");
    private const string Avito = "https://www.avito.ru/pushkino/zemelnye_uchastki?q=земля";
    private const string Cian = "https://www.cian.ru/cat.php?deal_type=sale&offer_type=suburban&region=175744&object_type%5B0%5D=3";
    private static ListingObservation Item(int id = 12345678, SourceSite source = SourceSite.Avito, DateTimeOffset? time = null) => new()
    {
        Source = source,
        ExternalId = id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Url = source == SourceSite.Avito ? $"https://www.avito.ru/pushkino/zemelnye_uchastki/uchastok_{id}" : $"https://www.cian.ru/sale/suburban/{id}/",
        ObservedAtUtc = time ?? DateTimeOffset.UtcNow,
        Title = TextValue.Read("Участок 10 сот."),
        Price = NumberValue.Read("1 000 000 ₽")
    };

    [TestMethod]
    public void UrlsPreserveFiltersAndMultipleValuesRejectSecrets()
    {
        NormalizedSearch a = SearchUrls.Normalize(Cian + "&location%5B1%5D=2&location%5B0%5D=1&p=4&context=private");
        StringAssert.Contains(a.Url, "location%5B0%5D=1"); StringAssert.Contains(a.Url, "location%5B1%5D=2");
        Assert.IsFalse(a.Url.Contains("private", StringComparison.Ordinal)); Assert.IsFalse(a.Url.Contains("p=4", StringComparison.Ordinal));
        Assert.AreEqual(a.Key, SearchUrls.Normalize(a.Url + "&p=7").Key);
        foreach (string invalid in new[] { Cian + "&token=secret", "https://www.cian.ru.evil/cat.php", "http://www.cian.ru/cat.php", "https://u:p@www.avito.ru/", Avito + "#fragment" })
            Assert.ThrowsExactly<ArgumentException>(() => SearchUrls.Normalize(invalid));
        Assert.ThrowsExactly<ArgumentException>(() => SearchUrls.Normalize(Cian, SourceSite.Avito));
        Assert.AreEqual(4, SearchUrls.PageNumber(SearchUrls.SafePage(a.Url + "&p=4", SourceSite.Cian)));
    }

    [TestMethod]
    public void SettingsBoundsAndSnapshotAreStable()
    {
        LocalStore store = new(Database()); CollectionSettings settings = new(); settings.Validate();
        Assert.AreEqual(10, store.Settings().MaxPages); Assert.AreEqual(24d, store.Settings().FreshnessHours);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => (settings with { CianTabs = 4 }).Validate());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => (settings with { FreshnessHours = double.NaN }).Validate());
        store.SaveLink("One", Avito); string batch = store.StartBatch(settings);
        store.SaveSettings(settings with { MaxPages = 2 });
        Assert.AreEqual(10, store.Jobs(batch).Single().Limit);
    }

    [TestMethod]
    public async Task TwentyLinksTenSelectedAreClaimedOnceAcrossSixWorkers()
    {
        LocalStore store = new(Database());
        for (int i = 0; i < 20; i++) store.SaveLink("Search " + i, i % 2 == 0 ? Avito + i : Cian + "&minprice=" + i, i < 10);
        string batch = store.StartBatch(new());
        var claims = await Task.WhenAll(Enumerable.Range(0, 6).Select(worker => Task.Run(() =>
        {
            List<CollectionJob> result = [];
            while (store.Claim(batch, worker % 2 == 0 ? SourceSite.Avito : SourceSite.Cian, "worker" + worker) is { } job) result.Add(job);
            return result.ToArray();
        })));
        CollectionJob[] all = claims.SelectMany(x => x).ToArray();
        Assert.AreEqual(10, all.Length); Assert.AreEqual(10, all.Select(x => x.Id).Distinct().Count());
        CollectionJob first = all[0];
        Assert.ThrowsExactly<InvalidOperationException>(() => store.SaveLink("Edit", first.Url, id: first.LinkId));
        Assert.ThrowsExactly<ArgumentException>(() => store.SaveLink("Duplicate", first.Url));
        store.StopBatch(batch);
        Assert.IsFalse(store.SavePage(first, 1, first.Url, [Item(source: first.Source)], true, new(NextKind.End), "stale"));
        Assert.AreEqual(0L, store.ReadListings(new()).Total);
    }

    [TestMethod]
    public void ResumeFourToSevenFreshSkipLargerLimitAndForcedOrExpiredRestart()
    {
        LocalStore store = new(Database()); store.SaveLink("Test", Avito);
        DateTimeOffset now = DateTimeOffset.UtcNow; CollectionSettings settings = new() { MaxPages = 4 };
        string batch = store.StartBatch(settings, now: now); CollectionJob job = store.Claim(batch, SourceSite.Avito, "tab")!;
        for (int page = 1; page <= 4; page++)
            Assert.IsTrue(store.SavePage(job, page, Avito + "&p=" + page, [Item()], true, new(NextKind.Next, Avito + "&p=" + (page + 1)), "complete"));
        store.SetState(job, JobState.LimitReached, "limit");
        Assert.AreEqual(JobState.SkippedFresh, store.Jobs(store.StartBatch(settings, now: now.AddMinutes(1))).Single().State);
        batch = store.StartBatch(settings with { MaxPages = 10 }, now: now.AddMinutes(2)); job = store.Claim(batch, SourceSite.Avito, "tab2")!;
        Assert.AreEqual(5, job.Page);
        for (int page = 5; page <= 7; page++)
            store.SavePage(job, page, Avito + "&p=" + page, [Item()], true, page == 7 ? new(NextKind.End) : new(NextKind.Next, Avito + "&p=" + (page + 1)), "complete");
        store.SetState(job, JobState.Completed, "end");
        Assert.AreEqual(JobState.SkippedFresh, store.Jobs(store.StartBatch(settings with { MaxPages = 100 }, now: now.AddMinutes(3))).Single().State);
        batch = store.StartBatch(settings, force: true, now: now.AddMinutes(4));
        Assert.AreEqual(1, store.Jobs(batch).Single().Page); store.StopBatch(batch);
        batch = store.StartBatch(settings, now: now.AddHours(25)); Assert.AreEqual(1, store.Jobs(batch).Single().Page);
    }

    [TestMethod]
    public void PartialPagesDoNotGrantCoverageMissingFieldsKeepKnownAndOlderObservationsDoNotOverwrite()
    {
        LocalStore store = new(Database()); store.SaveLink("Test", Avito); string batch = store.StartBatch(new());
        CollectionJob job = store.Claim(batch, SourceSite.Avito, "one")!; DateTimeOffset now = DateTimeOffset.UtcNow;
        store.SavePage(job, 1, Avito, [Item(time: now)], false, null, "partial");
        store.SavePage(job, 1, Avito, [Item(time: now.AddSeconds(1)) with { Price = NumberValue.Read(null) }], false, null, "missing");
        store.SavePage(job, 1, Avito, [Item(time: now.AddSeconds(-1)) with { Price = NumberValue.Read("5 ₽") }], false, null, "older");
        Assert.AreEqual(1000000m, store.ReadListings(new()).Rows.Single().Observation.Price.Parsed);
        Assert.AreEqual(3, store.History(SourceSite.Avito, "12345678").Length);
        store.StopBatch(batch);
        Assert.AreEqual(1, store.Jobs(store.StartBatch(new())).Single().Page);
    }

    [TestMethod]
    public void MigrationBacksUpLegacyDatabasePreservesHistoryAndRejectsFutureSchema()
    {
        string path = Database(); ListingStore legacy = new(path); string run = legacy.StartRun(Avito, 1);
        SearchParseResult parsed = SearchParser.Parse(new(false, false, false, false, false, true,
            [new("12345678", Item().Url, "Участок 10 сот.", "100 000 ₽", "Москва", "Сегодня")]), null, null, DateTimeOffset.UtcNow);
        legacy.Save(run, 1, DateTimeOffset.UtcNow, parsed.Listings);
        LocalStore modern = new(path); Assert.IsTrue(File.Exists(modern.MigrationBackup));
        Assert.AreEqual(1L, modern.ReadListings(new()).Total); Assert.AreEqual(1, modern.History(SourceSite.Avito, "12345678").Length);
        Assert.AreEqual("Legacy", modern.ReadListings(new()).Rows[0].Observation.Provenance);
        Assert.AreEqual(1, legacy.ReadListings().Length); Assert.IsNull(new LocalStore(path).MigrationBackup);
        using SqliteConnection db = new("Data Source=" + path); db.Open();
        using SqliteCommand cmd = db.CreateCommand(); cmd.CommandText = "PRAGMA user_version=3"; cmd.ExecuteNonQuery();
        Assert.ThrowsExactly<InvalidOperationException>(() => new LocalStore(path));
    }

    [TestMethod]
    public void BrokenMigrationRollsBackAndInstanceOwnershipProtectsRecovery()
    {
        string path = Database(); ListingStore legacy = new(path);
        using (SqliteConnection db = new("Data Source=" + path))
        {
            db.Open(); using SqliteCommand cmd = db.CreateCommand(); cmd.CommandText = "INSERT INTO listings VALUES('bad','2026-01-01','2026-01-01','broken')"; cmd.ExecuteNonQuery();
        }
        Assert.ThrowsExactly<System.Text.Json.JsonException>(() => new LocalStore(path));
        using (SqliteConnection db = new("Data Source=" + path))
        {
            db.Open(); using SqliteCommand cmd = db.CreateCommand(); cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE name='local_links'";
            Assert.AreEqual(0L, cmd.ExecuteScalar());
        }
        using InstanceGuard first = new(path);
        Assert.ThrowsExactly<InvalidOperationException>(() => new InstanceGuard(path));
    }

    [TestMethod]
    public void SourceKeysAssociationsAndCyrillicFiltersStayIndependent()
    {
        LocalStore store = new(Database());
        SearchLink a = store.SaveLink("Avito", Avito), b = store.SaveLink("Avito2", Avito + "2"), c = store.SaveLink("Cian", Cian);
        string batch = store.StartBatch(new());
        CollectionJob first = store.Claim(batch, SourceSite.Avito, "A")!, second = store.Claim(batch, SourceSite.Avito, "B")!, third = store.Claim(batch, SourceSite.Cian, "C")!;
        store.SavePage(first, 1, first.Url, [Item()], true, new(NextKind.End), "done");
        store.SavePage(second, 1, second.Url, [Item()], true, new(NextKind.End), "done");
        store.SavePage(third, 1, third.Url, [Item(source: SourceSite.Cian) with { Price = NumberValue.Read("200 ₽") }], true, new(NextKind.End), "done");
        Assert.AreEqual(2L, store.ReadListings(new()).Total);
        Assert.AreEqual(1L, store.ReadListings(new(LinkId: a.Id)).Total); Assert.AreEqual(1L, store.ReadListings(new(LinkId: b.Id)).Total);
        Assert.AreEqual(SourceSite.Cian, store.ReadListings(new(LinkId: c.Id)).Rows.Single().Source);
        Assert.AreEqual(200m, store.ReadListings(new(JobId: third.Id)).Rows.Single().Observation.Price.Parsed);
        Assert.AreEqual(2L, store.ReadListings(new("УЧАСТОК")).Total);
        Assert.AreEqual(1L, store.ReadListings(new(Source: SourceSite.Avito)).Total);
        Assert.AreEqual(1, store.History(SourceSite.Cian, "12345678").Length);
    }
    [TestMethod]
    public void FailedPageCommitRollsBackAndRecoveryInvalidatesOwnershipWithoutAutomaticRun()
    {
        string path = Database(); LocalStore store = new(path); store.SaveLink("Test", Avito);
        CollectionJob job = store.Claim(store.StartBatch(new()), SourceSite.Avito, "old process")!;
        Assert.ThrowsExactly<ArgumentException>(() => store.SavePage(job, 1, Avito, [Item(), Item(source: SourceSite.Cian)], true, new(NextKind.End), "bad"));
        Assert.AreEqual(0L, store.ReadListings(new()).Total); Assert.AreEqual(0, store.Journal(job.Id).Length);
        store.SavePage(job, 1, Avito, [Item()], false, null, "partial");
        LocalStore restarted = new(path); restarted.RecoverInterrupted();
        Assert.AreEqual(JobState.StoppedInterrupted, restarted.Jobs().Single().State);
        Assert.IsFalse(restarted.SavePage(job, 1, Avito, [Item()], true, new(NextKind.End), "stale"));
        Assert.IsNull(restarted.Claim(job.BatchId, SourceSite.Avito, "new process"));
        Assert.AreEqual(1, restarted.Jobs(restarted.StartBatch(new())).Single().Page);
    }
    [TestMethod]
    public void TenThousandRecordsUseSqlPagingAndStreamingExportWithIdempotentResults()
    {
        LocalStore store = new(Database()); store.SaveLink("Load", Avito); string batch = store.StartBatch(new());
        CollectionJob job = store.Claim(batch, SourceSite.Avito, "load")!; DateTimeOffset now = DateTimeOffset.UtcNow;
        ListingObservation[] items = Enumerable.Range(10000000, 10000).Select(i => Item(i, time: now)).ToArray();
        Stopwatch timer = Stopwatch.StartNew(); store.SavePage(job, 1, Avito, items, true, new(NextKind.End), "load");
        Console.WriteLine($"Write 10000: {timer.ElapsedMilliseconds} ms"); timer.Restart();
        ListingPage page = store.ReadListings(new(Offset: 9900)); Console.WriteLine($"SQL count and page100: {timer.ElapsedMilliseconds} ms");
        Assert.AreEqual(10000L, page.Total); Assert.AreEqual(100, page.Rows.Length);
        store.SavePage(job, 1, Avito, [items[0]], true, new(NextKind.End), "repeat");
        Assert.AreEqual(1, store.History(SourceSite.Avito, items[0].ExternalId).Length);
        using MemoryStream stream = new(); store.ExportJob(job.Id, stream);
        using var document = System.Text.Json.JsonDocument.Parse(stream.ToArray());
        Assert.AreEqual(10000, document.RootElement.GetProperty("observations").GetArrayLength());
    }
}
