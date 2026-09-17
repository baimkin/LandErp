using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using LandErp.Collector.Contracts.V1;
using LandErp.ParserSpike.ServerIntegration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.ParserSpike.Tests;

[TestClass]
public sealed class ServerTransportTests
{
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
