using LandErp.Collector.Contracts.V1;
using LandErp.ParserSpike.LocalCollection;
using LandErp.ParserSpike.ServerIntegration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.ParserSpike.Tests;

[TestClass]
public sealed class CollectionResultClassifierTests
{
    [TestMethod]
    public void ConfirmedEndIsSuccessAndCountHintMismatchIsOnlyAWarning()
    {
        CollectionJob job = Job(JobState.Completed, limit: 10);
        ObservationEnvelope[] observations = Enumerable.Range(1, 52).Select(Observation).ToArray();
        PageJournal[] journal =
        [
            new(job.Id, 1, job.Url, DateTimeOffset.UtcNow, 25, "done") { Completed = true },
            new(job.Id, 2, job.Url, DateTimeOffset.UtcNow, 27, "done") { Completed = true }
        ];
        CollectionResult[] results = CollectionResultClassifier.Build(Guid.CreateVersion7(), Guid.CreateVersion7(), job,
            observations, journal, new("draw", true, true, [], 98, 7),
            new(CollectionCompletionKind.Success, true, true, 4, "", ["MAP_COORDINATES_MISSING"]));

        Assert.IsTrue(results.Where(item => !item.Final).All(item => item.Observations.Length <= 25));
        Assert.AreEqual(52, results.Where(item => !item.Final).Sum(item => item.Observations.Length));
        CollectionResult final = results.Single(item => item.Final);
        Assert.AreEqual(CollectionOutcome.Success, final.Outcome);
        Assert.AreEqual(CollectionResultReasonCodes.CountHintMismatch, final.ReasonCode);
        CollectionAssert.Contains(final.Warnings!, CollectionResultReasonCodes.CountHintMismatch);
        Assert.AreEqual(52, final.Coverage!.UniqueObserved);
        Assert.AreEqual(98, final.Coverage.SourceCountHint);
        Assert.IsTrue(final.Coverage.EndReached); Assert.IsTrue(final.Coverage.LoadingCompleted);
        Assert.AreEqual(4, final.Coverage.StableRounds); Assert.AreEqual(2, final.Coverage.CompletedPages);
        Assert.AreEqual(10, final.Coverage.RequestedPageLimit); Assert.AreEqual(7, final.Coverage.ResponseBatches);
    }

    [TestMethod]
    public void PartialAndSourceErrorKeepEveryObservationInDurableStablePackets()
    {
        foreach (CollectionCompletionFacts facts in new CollectionCompletionFacts[]
        {
            new(CollectionCompletionKind.Partial, false, false, 2, CollectionResultReasonCodes.LoadingInterrupted, []),
            new(CollectionCompletionKind.SourceError, false, false, 0, CollectionResultReasonCodes.SourceUnavailable, [])
        })
        {
            CollectionJob job = Job(JobState.Failed, 3); Guid serverJob = Guid.CreateVersion7(), lease = Guid.CreateVersion7();
            CollectionResult[] built = CollectionResultClassifier.Build(serverJob, lease, job,
                Enumerable.Range(1, 31).Select(Observation).ToArray(), [], null, facts);
            string path = Path.Combine(Path.GetTempPath(), "LandErp-AdditiveOutbox", Guid.NewGuid().ToString("N"), "outbox.sqlite");
            ServerOutbox outbox = new(path); outbox.EnqueueMany(built);
            Guid[] first = outbox.Pending().Select(item => item.Result.ResultId).ToArray();
            Guid[] second = new ServerOutbox(path).Pending().Select(item => item.Result.ResultId).ToArray();

            CollectionAssert.AreEqual(first, second);
            Assert.AreEqual(31, outbox.Pending().Where(item => !item.Result.Final).Sum(item => item.Result.Observations.Length));
            Assert.AreEqual(facts.Kind == CollectionCompletionKind.Partial ? CollectionOutcome.Partial : CollectionOutcome.SourceError,
                outbox.Pending().Single(item => item.Result.Final).Result.Outcome);
        }
    }

    [TestMethod]
    public void CompletionFactsSurviveLocalStoreRestart()
    {
        string path = Path.Combine(Path.GetTempPath(), "LandErp-CompletionFacts", Guid.NewGuid().ToString("N"), "local.sqlite");
        LocalStore store = new(path); store.SaveLink("Search", "https://www.avito.ru/moskva/zemelnye_uchastki");
        CollectionJob job = store.Claim(store.StartBatch(new()), SourceSite.Avito, "test")!;
        CollectionCompletionFacts facts = new(CollectionCompletionKind.Partial, false, true, 3,
            CollectionResultReasonCodes.EndNotConfirmed, [CollectionResultReasonCodes.CountHintMismatch]);
        Assert.IsTrue(store.Finish(job, JobState.Failed, "partial", facts));

        CollectionCompletionFacts saved = new LocalStore(path).Completion(job.Id)!;
        Assert.AreEqual(facts.Kind, saved.Kind); Assert.AreEqual(facts.EndReached, saved.EndReached);
        Assert.AreEqual(facts.LoadingCompleted, saved.LoadingCompleted); Assert.AreEqual(facts.StableRounds, saved.StableRounds);
        Assert.AreEqual(facts.ReasonCode, saved.ReasonCode); CollectionAssert.AreEqual(facts.Warnings, saved.Warnings);
    }

    [TestMethod]
    [DataRow(CollectionCompletionKind.Success, CollectionOutcome.Success)]
    [DataRow(CollectionCompletionKind.LimitReached, CollectionOutcome.LimitReached)]
    [DataRow(CollectionCompletionKind.Partial, CollectionOutcome.Partial)]
    [DataRow(CollectionCompletionKind.RateLimited, CollectionOutcome.RateLimited)]
    [DataRow(CollectionCompletionKind.SourceError, CollectionOutcome.SourceError)]
    [DataRow(CollectionCompletionKind.Interrupted, CollectionOutcome.Interrupted)]
    [DataRow(CollectionCompletionKind.Captcha, CollectionOutcome.Captcha)]
    [DataRow(CollectionCompletionKind.AuthenticationRequired, CollectionOutcome.AuthenticationRequired)]
    public void EveryTerminalKindMapsToApprovedOutcome(CollectionCompletionKind kind, CollectionOutcome expected)
    {
        bool completed = kind == CollectionCompletionKind.Success;
        CollectionResult final = CollectionResultClassifier.Build(Guid.CreateVersion7(), Guid.CreateVersion7(),
            Job(completed ? JobState.Completed : JobState.Failed, 1), [], [], null,
            new(kind, completed, completed, completed ? 3 : 0, "", [])).Single();
        Assert.AreEqual(expected, final.Outcome);
    }

    private static CollectionJob Job(JobState state, int limit) => new("local-job", "batch", "server-link", 1,
        SourceSite.Avito, "https://www.avito.ru/moskva/zemelnye_uchastki", "pass", 1, limit, state, "", "worker", "token", DateTimeOffset.UtcNow);
    private static ObservationEnvelope Observation(int id) => new("observation-" + id, new()
    {
        Source = ListingSource.Avito,
        ExternalId = id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Url = "https://www.avito.ru/item/" + id,
        ObservedAt = DateTimeOffset.UtcNow,
        AdapterVersion = "test",
        Provenance = "test"
    });
}
