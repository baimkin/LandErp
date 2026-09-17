using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using LandErp.Collector.Contracts.V1;
using LandErp.ParserSpike.ServerIntegration;
using LandErp.ParserSpike.LocalCollection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.ParserSpike.Tests;

[TestClass]
public sealed class ServerTransportTests
{
    [TestMethod]
    public async Task NewActivationCodeExchangesSecretWithoutBearerAndValidatesOrigin()
    {
        Guid id = Guid.CreateVersion7();
        string code = "LDP1." + Convert.ToBase64String(Encoding.UTF8.GetBytes($"https://server.test/|{id:N}|{new string('B', 64)}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        ServerConnection activation = ServerAdapter.ParseActivationCode(code);
        using HttpClient http = new(new ReplyHandler(request =>
        {
            Assert.AreEqual("/api/collector/v1/activation", request.RequestUri!.AbsolutePath);
            Assert.IsNull(request.Headers.Authorization);
            AgentActivation body = JsonSerializer.Deserialize<AgentActivation>(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult(), CollectionJson.Options)!;
            Assert.AreEqual(id, body.AgentId); Assert.AreEqual(new string('B', 64), body.ActivationSecret);
            return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new AgentActivationReceipt(id, new string('C', 64), 1, "PC"), CollectionJson.Options));
        }));
        ServerConnection issued = await new ServerAdapter(http, activation).ActivateAsync(CancellationToken.None);
        Assert.AreEqual(new string('C', 64), issued.Token);
        string invalid = "LDP1." + Convert.ToBase64String(Encoding.UTF8.GetBytes($"http://server.test/|{id:N}|{new string('B', 64)}"));
        Assert.ThrowsExactly<ArgumentException>(() => ServerAdapter.ParseActivationCode(invalid));
    }

    [TestMethod]
    public async Task StopDuringClaimDoesNotOpenBrowserAndCompletesLeaseAsInterrupted()
    {
        string root = Path.Combine(Path.GetTempPath(), "LandErp-StopClaim", Guid.NewGuid().ToString("N"));
        LocalStore store = new(Path.Combine(root, "local.sqlite"));
        NoSessions sessions = new(); await using QueueRunner runner = new(store, sessions);
        ServerOutbox outbox = new(Path.Combine(root, "outbox.sqlite"));
        ServerCoordinator? coordinator = null;
        CollectionWork work = new(Guid.CreateVersion7(), Guid.CreateVersion7(), DateTimeOffset.UtcNow.AddMinutes(3), ListingSource.Avito, "https://www.avito.ru/moskva/zemelnye_uchastki", 1, "Search");
        int interrupted = 0;
        using HttpClient http = new(new ReplyHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("work/claim", StringComparison.Ordinal))
            { coordinator!.AcceptNewWork = false; return Json(HttpStatusCode.OK, JsonSerializer.Serialize(work, CollectionJson.Options)); }
            if (request.RequestUri.AbsolutePath.EndsWith("results", StringComparison.Ordinal))
            {
                CollectionResult result = JsonSerializer.Deserialize<CollectionResult>(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult(), CollectionJson.Options)!;
                Assert.AreEqual(CollectionOutcome.Interrupted, result.Outcome); Assert.AreEqual(0, result.Observations.Length); interrupted++;
                return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new CollectionReceipt(result.ResultId, "Interrupted", 0, 0), CollectionJson.Options));
            }
            return Json(HttpStatusCode.OK, "{}");
        }));
        await using (coordinator = new(store, runner, outbox, Adapter(http)))
        {
            await coordinator.TickAsync(CancellationToken.None);
            Assert.AreEqual(0, sessions.Created); Assert.AreEqual(0, store.Jobs().Length);
            await coordinator.TickAsync(CancellationToken.None);
            Assert.AreEqual(1, interrupted); Assert.IsNull(outbox.ReadWork()); Assert.AreEqual(0, outbox.Pending().Length);
        }
    }

    private sealed class NoSessions : ISourceSessions
    {
        public int Created;
        public Task<ISourcePage> CreatePageAsync(SourceSite source, CollectionSettings settings, CancellationToken cancellationToken)
        { Created++; throw new InvalidOperationException("Browser must not start after Stop"); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [TestMethod]
    public async Task ExpiredLeaseRetainsRejectedPayloadWithoutBlockingFutureWork()
    {
        string root = Path.Combine(Path.GetTempPath(), "LandErp-ExpiredLease", Guid.NewGuid().ToString("N"));
        LocalStore store = new(Path.Combine(root, "local.sqlite"));
        NoSessions sessions = new(); await using QueueRunner runner = new(store, sessions);
        ServerOutbox outbox = new(Path.Combine(root, "outbox.sqlite"));
        CollectionWork work = new(Guid.CreateVersion7(), Guid.CreateVersion7(), DateTimeOffset.UtcNow.AddMinutes(-1), ListingSource.Avito,
            "https://www.avito.ru/moskva/zemelnye_uchastki", 1, "Expired");
        outbox.SaveWork(new(work, null));
        using HttpClient http = new(new ReplyHandler(_ => Problem(HttpStatusCode.Conflict, "LEASE_EXPIRED")));
        await using ServerCoordinator coordinator = new(store, runner, outbox, Adapter(http)) { AcceptNewWork = false };
        await coordinator.TickAsync(CancellationToken.None);
        Assert.AreEqual(0, sessions.Created); Assert.IsNull(outbox.ReadWork()); Assert.AreEqual(0, outbox.Pending().Length);
        Assert.IsTrue(outbox.HasDelivery(work.JobId, work.LeaseId));
        StringAssert.Contains(coordinator.Status, "сохранены на компьютере");
    }

    [TestMethod]
    public async Task ProblemDetailsMachineCodeIsPreservedInsteadOfCollapsedByStatus()
    {
        using HttpClient http = new(new ReplyHandler(_ => Problem(HttpStatusCode.Conflict, CollectorErrorCodes.IdempotencyConflict)));
        ServerAdapter adapter = Adapter(http);

        ServerDeliveryException exception = await Assert.ThrowsExactlyAsync<ServerDeliveryException>(() =>
            adapter.SendResultAsync(Result(Guid.CreateVersion7()), CancellationToken.None));

        Assert.AreEqual(CollectorErrorCodes.IdempotencyConflict, exception.Code);
        Assert.IsFalse(exception.Retryable);
    }

    [TestMethod]
    public async Task PermanentFailureOfOneJobDoesNotBlockAnotherJob()
    {
        Guid blockedJob = Guid.CreateVersion7();
        Guid acceptedJob = Guid.CreateVersion7();
        using HttpClient http = new(new ReplyHandler(request =>
        {
            CollectionResult result = JsonSerializer.Deserialize<CollectionResult>(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult(), CollectionJson.Options)!;
            if (result.JobId == blockedJob) return Problem(HttpStatusCode.Conflict, CollectorErrorCodes.LeaseExpiredOrReplaced);
            CollectionReceipt receipt = new(result.ResultId, "Completed", 0, 0);
            return Json(HttpStatusCode.OK, JsonSerializer.Serialize(receipt, CollectionJson.Options));
        }));
        ServerOutbox outbox = new(Path.Combine(Path.GetTempPath(), "LandErp-OutboxTests", Guid.NewGuid().ToString("N"), "outbox.sqlite"));
        outbox.Enqueue(Result(blockedJob)); outbox.Enqueue(Result(acceptedJob));

        ServerDeliveryException exception = await Assert.ThrowsExactlyAsync<ServerDeliveryException>(() =>
            outbox.FlushAsync(Adapter(http), CancellationToken.None));

        Assert.AreEqual(CollectorErrorCodes.LeaseExpiredOrReplaced, exception.Code);
        Assert.AreEqual(blockedJob, outbox.Pending().Single().Result.JobId);
    }

    [TestMethod]
    public void EmptyOutboxMayMoveButPendingDataStaysBoundToItsServerAndAgent()
    {
        ServerOutbox outbox = NewOutbox();
        ServerConnection first = Connection("https://first.test/");
        ServerConnection second = Connection("https://second.test/");

        outbox.Bind(first, allowUnboundData: false);
        outbox.Bind(second, allowUnboundData: false);
        outbox.Enqueue(Result(Guid.CreateVersion7()));

        InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            outbox.Bind(first, allowUnboundData: false));
        StringAssert.Contains(exception.Message, "незавершённое");
        outbox.Bind(second, allowUnboundData: false);
    }

    private static ServerAdapter Adapter(HttpClient http) => new(http,
        new(new Uri("https://server.test/"), Guid.CreateVersion7(), new string('A', 64)));
    private static ServerConnection Connection(string origin) =>
        new(new Uri(origin), Guid.CreateVersion7(), new string('A', 64));
    private static ServerOutbox NewOutbox() => new(Path.Combine(Path.GetTempPath(), "LandErp-OutboxTests",
        Guid.NewGuid().ToString("N"), "outbox.sqlite"));
    private static CollectionResult Result(Guid jobId) => new(Guid.CreateVersion7(), jobId, Guid.CreateVersion7(),
        CollectionOutcome.Success, [], true);
    private static HttpResponseMessage Problem(HttpStatusCode status, string code) => Json(status,
        JsonSerializer.Serialize(new { type = "about:blank", title = "Rejected", status = (int)status, code }));
    private static HttpResponseMessage Json(HttpStatusCode status, string value) => new(status)
    { Content = new StringContent(value, Encoding.UTF8, "application/problem+json") };

    private sealed class ReplyHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(reply(request));
    }
}
