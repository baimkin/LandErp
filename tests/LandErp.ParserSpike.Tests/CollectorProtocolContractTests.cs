using System.Text.Json;
using LandErp.Collector.Contracts.V1;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.ParserSpike.Tests;

[TestClass]
public sealed class CollectorProtocolContractTests
{
    private static readonly string[] FixedTimes = ["09:00", "18:00"];

    [TestMethod]
    public void LegacyHeartbeatRemainsReadableWithAdditiveRuntimeFields()
    {
        Guid jobId = Guid.Parse("0199f54b-8bc0-7a2a-8f51-1477d047b4cc");
        Guid leaseId = Guid.Parse("0199f54b-b14b-7daf-b5e3-8a92ee83ef3f");
        string legacy = $$"""{"jobId":"{{jobId}}","leaseId":"{{leaseId}}","sourceStatus":"Captcha"}""";

        AgentHeartbeat heartbeat = JsonSerializer.Deserialize<AgentHeartbeat>(legacy, CollectionJson.Options)!;

        Assert.AreEqual(jobId, heartbeat.JobId);
        Assert.AreEqual(leaseId, heartbeat.LeaseId);
        Assert.AreEqual(CollectionOutcome.Captcha, heartbeat.SourceStatus);
        Assert.IsNull(heartbeat.RuntimeState);
        Assert.IsNull(heartbeat.SourceState);
        Assert.IsNull(heartbeat.Progress);
    }

    [TestMethod]
    public void RuntimeHeartbeatUsesExplicitReadyStateAndStructuredProgress()
    {
        DateTimeOffset usefulAt = new(2026, 9, 17, 9, 30, 0, TimeSpan.Zero);
        AgentHeartbeat original = new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            RuntimeState: AgentRuntimeState.Parsing,
            SourceState: SourceRuntimeState.Ready,
            Progress: new(2, 10, 37, 120, CollectionProgressPhase.ReadingPage, usefulAt));

        string json = JsonSerializer.Serialize(original, CollectionJson.Options);
        AgentHeartbeat copy = JsonSerializer.Deserialize<AgentHeartbeat>(json, CollectionJson.Options)!;

        Assert.AreEqual(AgentRuntimeState.Parsing, copy.RuntimeState);
        Assert.AreEqual(SourceRuntimeState.Ready, copy.SourceState);
        Assert.AreEqual(2, copy.Progress!.Page);
        Assert.AreEqual(37, copy.Progress.ProcessedCount);
        Assert.AreEqual(usefulAt, copy.Progress.LastUsefulActionAt);
    }

    [TestMethod]
    public void ServerSearchCommandCarriesOneLinkAndChosenServerGroup()
    {
        Guid groupId = Guid.CreateVersion7();
        CreateCollectorSearch command = new(
            Guid.CreateVersion7(),
            "Пушкино — участок",
            ListingSource.Avito,
            "https://www.avito.ru/pushkino/zemelnye_uchastki",
            5,
            groupId,
            new(CollectorScheduleKind.FixedTimes, FixedTimes: FixedTimes));

        string json = JsonSerializer.Serialize(command, CollectionJson.Options);
        CreateCollectorSearch copy = JsonSerializer.Deserialize<CreateCollectorSearch>(json, CollectionJson.Options)!;

        Assert.AreEqual(groupId, copy.GroupId);
        Assert.AreEqual(command.Url, copy.Url);
        Assert.AreEqual(CollectorScheduleKind.FixedTimes, copy.Schedule.Kind);
        CollectionAssert.AreEqual(FixedTimes, copy.Schedule.FixedTimes);
    }

    [TestMethod]
    public void OneConnectionCodeCarriesValidatedOriginAndMachineCredential()
    {
        Guid agentId = Guid.CreateVersion7();
        string token = new('A', 64);
        string code = CollectorConnectionCode.Create(new Uri("https://server.test/"), agentId, token);

        CollectorConnectionEnvelope copy = CollectorConnectionCode.Parse(code);

        Assert.AreEqual("https://server.test/", copy.ServerOrigin);
        Assert.AreEqual(agentId, copy.AgentId);
        Assert.AreEqual(token, copy.Token);
        Assert.ThrowsExactly<ArgumentException>(() => CollectorConnectionCode.Parse("not-a-code"));
        Assert.ThrowsExactly<ArgumentException>(() => CollectorConnectionCode.Create(new Uri("http://server.test/"), agentId, token));
    }
}
