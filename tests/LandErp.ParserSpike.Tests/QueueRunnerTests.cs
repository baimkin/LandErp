using LandErp.ParserSpike.LocalCollection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.ParserSpike.Tests;

[TestClass]
public sealed class QueueRunnerTests
{
    private static LocalStore Store() => new(Path.Combine(Path.GetTempPath(), "LandErp-QueueTests", Guid.NewGuid().ToString("N"), "queue.sqlite"));
    private static CollectionSettings Fast => new() { StabilitySeconds = 1, PageIntervalSeconds = 1, LinkIntervalSeconds = 1 };
    private sealed class FakePage(SourceSite source, FakeSessions sessions) : ISourcePage
    {
        public SourceSite Source => source;
        public string CurrentUrl { get; private set; } = "";
        public Task OpenAsync(string url, CancellationToken cancellationToken) { CurrentUrl = url; Interlocked.Increment(ref sessions.Opened); return Task.CompletedTask; }
        public Task<PageObservation> ReadAsync(CancellationToken cancellationToken)
        {
            if (sessions.Timeout) throw new TimeoutException("cookie=private-test-secret");
            if (source == SourceSite.Avito && sessions.Captcha) return Task.FromResult(new PageObservation(PageKind.Captcha, [], []));
            return Task.FromResult(new PageObservation(PageKind.SearchResults, [new()
            {
                Source = source, ExternalId = "12345678", Url = source == SourceSite.Avito ? "https://www.avito.ru/item_12345678" : "https://www.cian.ru/sale/suburban/12345678/",
                ObservedAtUtc = DateTimeOffset.UtcNow, Title = TextValue.Read("Участок 10 сот."), Price = NumberValue.Read("100 ₽")
            }], []));
        }
        public Task<bool> WheelAsync(CollectionSettings settings, CancellationToken cancellationToken) { Interlocked.Increment(ref sessions.Wheels); return Task.FromResult(true); }
        public Task<Pagination> NextAsync(CancellationToken cancellationToken) => Task.FromResult(new Pagination(NextKind.End));
        public Task FollowAsync(Pagination pagination, CancellationToken cancellationToken) => throw new InvalidOperationException("NOT_EXPECTED");
        public Task ActivateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() { Interlocked.Decrement(ref sessions.Pages); return ValueTask.CompletedTask; }
    }
    private sealed class FakeSessions : ISourceSessions
    {
        public volatile bool Captcha;
        public bool Timeout;
        public int Opened;
        public int Wheels;
        public int Pages;
        public int Maximum;
        public Task<ISourcePage> CreatePageAsync(SourceSite source, CollectionSettings settings, CancellationToken cancellationToken)
        {
            int count = Interlocked.Increment(ref Pages); Maximum = Math.Max(Maximum, count);
            return Task.FromResult<ISourcePage>(new FakePage(source, this));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    [TestMethod]
    public async Task BrowserTimeoutIdentifiesActionWithoutLoggingExceptionPayload()
    {
        LocalStore store = Store();
        store.SaveLink("Avito", "https://www.avito.ru/pushkino/zemelnye_uchastki");
        DiagnosticJournal journal = new(Path.Combine(Path.GetTempPath(), "LandErp-Timeout", Guid.NewGuid().ToString("N"), "events.jsonl"));
        await using QueueRunner runner = new(store, new FakeSessions { Timeout = true }, journal);
        await runner.StartAsync(Fast);
        CollectionJob job = store.Jobs().Single();
        Assert.AreEqual(JobState.Failed, job.State);
        StringAssert.Contains(job.Reason, "Чтение выдачи");
        DiagnosticEvent error = journal.Read().Single(x => x.Outcome == "Ошибка");
        Assert.AreEqual("TimeoutException", error.ErrorType);
        Assert.AreEqual("Чтение выдачи", error.Action);
        Assert.IsFalse(File.ReadAllText(journal.Path).Contains("private-test-secret", StringComparison.Ordinal));
    }
    [TestMethod]
    public async Task CaptchaBlocksAllAvitoWorkersWhileCianCompletesAndRequiresManualResume()
    {
        LocalStore store = Store(); store.SaveLink("Avito", "https://www.avito.ru/pushkino/zemelnye_uchastki");
        store.SaveLink("Avito2", "https://www.avito.ru/pushkino/zemelnye_uchastki?q=2");
        store.SaveLink("Cian", "https://www.cian.ru/cat.php?region=175744");
        FakeSessions sessions = new() { Captcha = true }; await using QueueRunner runner = new(store, sessions);
        TaskCompletionSource blocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        runner.ManualActionRequired += (_, _) => blocked.TrySetResult();
        Task run = runner.StartAsync(Fast with { AvitoTabs = 3, CianTabs = 3 });
        await blocked.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(2200);
        Assert.IsTrue(store.Jobs().Any(x => x.Source == SourceSite.Cian && x.State == JobState.Completed));
        Assert.IsFalse(store.Jobs().Any(x => x.Source == SourceSite.Avito && x.State == JobState.Completed));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => runner.ResumeSourceAsync(SourceSite.Avito));
        sessions.Captcha = false; await runner.ResumeSourceAsync(SourceSite.Avito);
        await run.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.IsTrue(store.Jobs().All(x => x.State == JobState.Completed)); Assert.IsTrue(sessions.Maximum <= 6);
    }
    [TestMethod]
    [DataRow("Open")]
    [DataRow("Read")]
    [DataRow("Wheel")]
    [DataRow("Next")]
    [DataRow("Follow")]
    public async Task StopDuringBrowserOperationsDoesNotGrantUnfinishedPageCoverage(string stage)
    {
        LocalStore store = Store(); store.SaveLink("One", "https://www.avito.ru/pushkino/zemelnye_uchastki");
        BlockingSessions sessions = new(stage); await using QueueRunner runner = new(store, sessions);
        Task run = runner.StartAsync(Fast);
        await sessions.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        runner.Stop(); await run.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.AreEqual(JobState.StoppedInterrupted, store.Jobs().Single().State);
        Assert.IsFalse(store.Journal(store.Jobs().Single().Id).Any(x => x.Completed && stage != "Follow"));
        string retry = store.StartBatch(Fast);
        Assert.AreEqual(stage == "Follow" ? 2 : 1, store.Jobs(retry).Single().Page);
    }
    private sealed class BlockingSessions(string stage) : ISourceSessions
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ISourcePage> CreatePageAsync(SourceSite source, CollectionSettings settings, CancellationToken cancellationToken) => Task.FromResult<ISourcePage>(new BlockingPage(this, stage));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class BlockingPage(BlockingSessions sessions, string stage) : ISourcePage
    {
        public SourceSite Source => SourceSite.Avito;
        public string CurrentUrl { get; private set; } = "";
        private async Task BlockAsync(string operation, CancellationToken token)
        {
            if (operation != stage) return;
            sessions.Entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }
        public async Task OpenAsync(string url, CancellationToken cancellationToken) { CurrentUrl = url; await BlockAsync("Open", cancellationToken); }
        public async Task<PageObservation> ReadAsync(CancellationToken cancellationToken) { await BlockAsync("Read", cancellationToken); return new(PageKind.SearchResults, [], []); }
        public async Task<bool> WheelAsync(CollectionSettings settings, CancellationToken cancellationToken) { await BlockAsync("Wheel", cancellationToken); return true; }
        public async Task<Pagination> NextAsync(CancellationToken cancellationToken) { await BlockAsync("Next", cancellationToken); return new(NextKind.Next, CurrentUrl + "?p=2", "a"); }
        public Task FollowAsync(Pagination pagination, CancellationToken cancellationToken) => BlockAsync("Follow", cancellationToken);
        public Task ActivateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    [TestMethod]
    public async Task ThreePlusThreeWorkerPagesExecuteWithoutDuplicateLinks()
    {
        LocalStore store = Store();
        for (int i = 0; i < 3; i++)
        {
            store.SaveLink("Avito" + i, "https://www.avito.ru/pushkino/zemelnye_uchastki?q=" + i);
            store.SaveLink("Cian" + i, "https://www.cian.ru/cat.php?region=175744&minprice=" + i);
        }
        FakeSessions sessions = new(); await using QueueRunner runner = new(store, sessions);
        await runner.StartAsync(Fast with { AvitoTabs = 3, CianTabs = 3 }).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.AreEqual(6, sessions.Maximum); Assert.AreEqual(6, sessions.Opened);
        Assert.AreEqual(6, store.Jobs().Count(x => x.State == JobState.Completed));
        Assert.AreEqual(6, store.Jobs().Select(x => x.LinkId).Distinct().Count());
    }
    [TestMethod]
    public async Task GlobalLimitOneStillProcessesBothSitesInSequence()
    {
        LocalStore store = Store(); store.SaveLink("Avito", "https://www.avito.ru/pushkino/zemelnye_uchastki");
        store.SaveLink("Cian", "https://www.cian.ru/cat.php?region=175744");
        FakeSessions sessions = new(); await using QueueRunner runner = new(store, sessions);
        await runner.StartAsync(Fast with { GlobalTabs = 1 }).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(2, store.Jobs().Count(x => x.State == JobState.Completed)); Assert.AreEqual(1, sessions.Maximum);
    }
    [TestMethod]
    public async Task UserPauseAndStopKeepPartialResultsAndCancelWaitingSource()
    {
        LocalStore store = Store(); store.SaveLink("One", "https://www.avito.ru/pushkino/zemelnye_uchastki");
        FakeSessions sessions = new(); await using QueueRunner runner = new(store, sessions);
        Task run = runner.StartAsync(Fast with { StabilitySeconds = 15, LoadWaitSeconds = 30 });
        for (int i = 0; i < 100 && sessions.Wheels == 0; i++) await Task.Delay(20);
        runner.Pause(); int wheels = sessions.Wheels; await Task.Delay(500); Assert.AreEqual(wheels, sessions.Wheels);
        Assert.AreEqual(JobState.PausedByUser, store.Jobs().Single().State);
        runner.Resume(); await Task.Delay(300); runner.Stop(); await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(JobState.StoppedInterrupted, store.Jobs().Single().State); Assert.AreEqual(1L, store.ReadListings(new()).Total);
        Assert.AreEqual(1, store.Jobs(store.StartBatch(Fast)).First(x => x.State == JobState.Pending).Page);
    }
}
