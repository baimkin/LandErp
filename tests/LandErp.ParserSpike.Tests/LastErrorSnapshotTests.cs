using LandErp.ParserSpike.LocalCollection;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.ParserSpike.Tests;

[TestClass]
public sealed class LastErrorSnapshotTests
{
    private const string Url = "https://www.avito.ru/moskva/zemelnye_uchastki";
    private static string Root() => Path.Combine(Path.GetTempPath(), "LandErp-LastError", Guid.NewGuid().ToString("N"));
    private static CollectionJob Job(LocalStore store)
    {
        store.SaveLink("Test", Url);
        return store.Claim(store.StartBatch(new()), SourceSite.Avito, "test")!;
    }
    private static LastErrorDetails Read(LastErrorSnapshot snapshot) => LocalJson.Read<LastErrorDetails>(File.ReadAllText(snapshot.DetailsPath));

    [TestMethod]
    public async Task LatestErrorReplacesPairAndUsesSafeMetadata()
    {
        string root = Root(); LocalStore store = new(Path.Combine(root, "test.sqlite"));
        CollectionJob job = Job(store); LastErrorSnapshot snapshot = new(Path.Combine(root, "diagnostics"));
        await snapshot.SaveAsync(job, new Page { Html = "<html>first</html>" }, "First failure", "SOURCE_UNAVAILABLE");
        await snapshot.SaveAsync(job, new Page { Html = "<html>second</html>", Url = Url + "?token=do-not-log" }, "Second failure", "LAYOUT_CHANGED");
        Assert.AreEqual("<html>second</html>", File.ReadAllText(snapshot.HtmlPath));
        LastErrorDetails details = Read(snapshot);
        Assert.AreEqual("Second failure", details.Reason); Assert.AreEqual("LAYOUT_CHANGED", details.ReasonCode);
        Assert.AreEqual("Saved", details.CaptureStatus); Assert.AreEqual("last-error.html", details.HtmlFile);
        Assert.IsFalse(File.ReadAllText(snapshot.DetailsPath).Contains("do-not-log", StringComparison.Ordinal));
        Assert.AreEqual(2, Directory.GetFiles(Path.GetDirectoryName(snapshot.HtmlPath)!).Length);
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("closed")]
    [DataRow("oversize")]
    public async Task UnavailableCaptureRemovesOldHtmlAndPreservesNewReason(string mode)
    {
        string root = Root(); CollectionJob job = Job(new(Path.Combine(root, "test.sqlite")));
        LastErrorSnapshot snapshot = new(Path.Combine(root, "diagnostics"));
        await snapshot.SaveAsync(job, new Page(), "Old", null);
        Page? page = mode == "null" ? null : new Page
        {
            Capture = mode == "closed" ? _ => throw new InvalidOperationException("token=do-not-log")
                : _ => Task.FromResult<string?>(new string('x', LastErrorSnapshot.MaximumHtmlCharacters + 1))
        };
        await snapshot.SaveAsync(job, page, "New", "SOURCE_UNAVAILABLE");
        Assert.IsFalse(File.Exists(snapshot.HtmlPath)); Assert.AreEqual("New", Read(snapshot).Reason);
        Assert.IsNull(Read(snapshot).HtmlFile);
        Assert.IsFalse(File.ReadAllText(snapshot.DetailsPath).Contains("do-not-log", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task LateOlderCaptureCannotReplaceNewerFailure()
    {
        string root = Root(); CollectionJob job = Job(new(Path.Combine(root, "test.sqlite")));
        LastErrorSnapshot snapshot = new(Path.Combine(root, "diagnostics"));
        TaskCompletionSource<string?> slow = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task older = snapshot.SaveAsync(job, new Page { Capture = _ => slow.Task }, "Old", null);
        await snapshot.SaveAsync(job, new Page { Html = "latest" }, "New", null);
        slow.SetResult("stale"); await older;
        Assert.AreEqual("latest", File.ReadAllText(snapshot.HtmlPath)); Assert.AreEqual("New", Read(snapshot).Reason);
    }

    [TestMethod]
    public async Task HungCaptureIsBoundedAndClearsOldHtml()
    {
        string root = Root(); CollectionJob job = Job(new(Path.Combine(root, "test.sqlite")));
        LastErrorSnapshot snapshot = new(Path.Combine(root, "diagnostics"));
        await snapshot.SaveAsync(job, new Page(), "Old", null);
        TaskCompletionSource<string?> hung = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await snapshot.SaveAsync(job, new Page { Capture = _ => hung.Task }, "Timeout", null).WaitAsync(TimeSpan.FromSeconds(6));
        Assert.AreEqual("Timeout", Read(snapshot).CaptureStatus); Assert.IsFalse(File.Exists(snapshot.HtmlPath));
        hung.SetResult("too late"); Assert.IsFalse(File.Exists(snapshot.HtmlPath));
    }

    [TestMethod]
    public async Task UnwritableDiagnosticsDoesNotFailCollection()
    {
        string root = Root(); CollectionJob job = Job(new(Path.Combine(root, "test.sqlite")));
        string blocked = Path.Combine(root, "not-directory"); File.WriteAllText(blocked, "existing file");
        await new LastErrorSnapshot(blocked).SaveAsync(job, new Page(), "Failure", null);
        Assert.AreEqual("existing file", File.ReadAllText(blocked));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SemanticFailureAndExceptionCaptureBeforeDisposalAndSuccessKeepsSnapshot(bool exception)
    {
        string root = Root(); LocalStore store = new(Path.Combine(root, "test.sqlite")); store.SaveLink("Test", Url);
        DiagnosticJournal journal = new(Path.Combine(root, "diagnostics"));
        Page page = new() { Fail = true, ThrowOnRead = exception };
        await using QueueRunner runner = new(store, new Sessions(page), journal);
        CollectionSettings settings = new() { ErrorPolicy = ErrorPolicy.Continue, StabilitySeconds = 1, PageIntervalSeconds = 1, LinkIntervalSeconds = 1 };
        await runner.StartAsync(settings).WaitAsync(TimeSpan.FromSeconds(12));
        Assert.AreEqual(JobState.Failed, store.Jobs().Single().State);
        Assert.AreEqual(1, page.Captures); Assert.IsTrue(page.Disposed);
        Assert.AreEqual("Saved", Read(journal.LastError).CaptureStatus);
        string metadata = File.ReadAllText(journal.LastError.DetailsPath);
        page.Fail = false; page.Disposed = false;
        await runner.StartAsync(settings, force: true).WaitAsync(TimeSpan.FromSeconds(12));
        Assert.AreEqual(JobState.Completed, store.Jobs().OrderByDescending(x => x.StartedAtUtc).First().State);
        Assert.AreEqual(metadata, File.ReadAllText(journal.LastError.DetailsPath)); Assert.AreEqual(1, page.Captures);
    }

    [TestMethod]
    [DataRow(PageKind.SourceError)]
    [DataRow(PageKind.RateLimited)]
    public async Task ServerSourceFailureCapturesHtmlAndRetainsCollectedObservations(PageKind kind)
    {
        string root = Root(); LocalStore store = new(Path.Combine(root, "test.sqlite")); store.SaveLink("Test", Url);
        DiagnosticJournal journal = new(Path.Combine(root, "diagnostics"));
        Page page = new() { Fail = true, PartialBeforeFailure = true, FailureKind = kind };
        await using QueueRunner runner = new(store, new Sessions(page), journal);
        await runner.StartAsync(new() { StabilitySeconds = 1, LinkIntervalSeconds = 1 }, serverManaged: true).WaitAsync(TimeSpan.FromSeconds(12));
        Assert.AreEqual(JobState.Failed, store.Jobs().Single().State);
        Assert.AreEqual(1L, store.ReadListings(new()).Total);
        Assert.AreEqual("Saved", Read(journal.LastError).CaptureStatus); Assert.AreEqual(1, page.Captures);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ErrorMetadataRetainsLastReadAndCurrentFlagsEvenIfHtmlFails(bool htmlFails)
    {
        string root = Root();
        CollectionJob job = Job(new(Path.Combine(root, "test.sqlite")));
        LastErrorSnapshot snapshot = new(Path.Combine(root, "diagnostics"));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        MapSelectionErrorEvidence evidence = new(
            new(now, Url, JsonSerializer.SerializeToElement(new { ResetVisible = false })),
            new(now.AddSeconds(1), Url, JsonSerializer.SerializeToElement(new { ResetVisible = true })), null);
        Page page = new()
        {
            Capture = htmlFails ? _ => Task.FromException<string?>(new InvalidOperationException("private-token")) : null,
            Selection = _ => Task.FromResult<MapSelectionErrorEvidence?>(evidence)
        };
        await snapshot.SaveAsync(job, page, "AREA_SELECTION_UNCONFIRMED", "END_NOT_CONFIRMED");
        var saved = Read(snapshot).MapSelection!;
        Assert.IsFalse(saved.LastRead!.State.GetProperty("ResetVisible").GetBoolean());
        Assert.IsTrue(saved.AtError!.State.GetProperty("ResetVisible").GetBoolean());
        Assert.AreEqual(now, saved.LastRead.CapturedUtc);
        Assert.DoesNotContain("private-token", File.ReadAllText(snapshot.DetailsPath));

        await snapshot.SaveAsync(job, new Page
        {
            Selection = _ => Task.FromException<MapSelectionErrorEvidence?>(new InvalidOperationException("private-token"))
        }, "Other", null);
        Assert.IsNull(Read(snapshot).MapSelection!.AtError);
        Assert.AreEqual("InvalidOperationException", Read(snapshot).MapSelection!.CaptureError);
        Assert.DoesNotContain("private-token", File.ReadAllText(snapshot.DetailsPath));
    }

    [TestMethod]
    public async Task SlowOlderSelectionCannotReplaceNewerSnapshot()
    {
        string root = Root();
        CollectionJob job = Job(new(Path.Combine(root, "test.sqlite")));
        LastErrorSnapshot snapshot = new(Path.Combine(root, "diagnostics"));
        TaskCompletionSource<MapSelectionErrorEvidence?> delayed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task older = snapshot.SaveAsync(job, new Page { Selection = _ => delayed.Task }, "Old", null);
        await snapshot.SaveAsync(job, new Page { Html = "new" }, "New", null);
        delayed.SetResult(new(null, null, "old-error"));
        await older;
        Assert.AreEqual("New", Read(snapshot).Reason);
        Assert.IsNull(Read(snapshot).MapSelection);
        Assert.AreEqual("new", File.ReadAllText(snapshot.HtmlPath));
    }

    private sealed class Sessions(Page page) : ISourceSessions
    {
        public Task<ISourcePage> CreatePageAsync(SourceSite source, CollectionSettings settings, CancellationToken cancellationToken) => Task.FromResult<ISourcePage>(page);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Page : ISourcePage
    {
        public SourceSite Source => SourceSite.Avito;
        public string Url { get; init; } = LastErrorSnapshotTests.Url;
        public string CurrentUrl => Url;
        public string Html { get; init; } = "<html><body>error</body></html>";
        public Func<CancellationToken, Task<string?>>? Capture { get; init; }
        public Func<CancellationToken, Task<MapSelectionErrorEvidence?>>? Selection { get; init; }
        public Task<MapSelectionErrorEvidence?> CaptureMapSelectionErrorAsync(CancellationToken cancellationToken)
            => Selection?.Invoke(cancellationToken) ?? Task.FromResult<MapSelectionErrorEvidence?>(null);
        public bool Fail { get; set; }
        public bool ThrowOnRead { get; init; }
        public bool PartialBeforeFailure { get; init; }
        public PageKind FailureKind { get; init; } = PageKind.SourceError;
        private int reads;
        public bool Disposed { get; set; }
        public int Captures { get; private set; }
        public Task<string?> CaptureErrorHtmlAsync(CancellationToken cancellationToken)
        {
            Assert.IsFalse(Disposed); Captures++;
            return Capture is null ? Task.FromResult<string?>(Html) : Capture(cancellationToken);
        }
        public Task OpenAsync(string url, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<PageObservation> ReadAsync(CancellationToken cancellationToken)
        {
            if (PartialBeforeFailure && reads++ == 0)
                return Task.FromResult(new PageObservation(PageKind.SearchResults, [new ListingObservation
                {
                    Source = Source, ExternalId = "12345678", Url = Url + "/item_12345678", ObservedAtUtc = DateTimeOffset.UtcNow
                }], []));
            if (Fail && ThrowOnRead) throw new TimeoutException("token=do-not-log");
            return Task.FromResult(new PageObservation(Fail ? FailureKind : PageKind.SearchResults, [], []));
        }
        public Task<bool> WheelAsync(CollectionSettings settings, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<Pagination> NextAsync(CancellationToken cancellationToken) => Task.FromResult(new Pagination(NextKind.End));
        public Task FollowAsync(Pagination pagination, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ActivateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
