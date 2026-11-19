using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.Collection.Contracts;
using LandErp.Application.Modules.Collection.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Collector.Contracts.V1;
using LandErp.Infrastructure.Modules.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass, TestCategory("PostgreSQL")]
public sealed class ParsingArchiveTests
{
    private static readonly string[] ExpectedActiveLabels = ["Действующий", "Без зоны"];
    [TestMethod]
    public async Task ArchivedEnabledSearchCannotEnqueueScheduleRetryOrClaimAndHistoryRemainsReadable()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        var clock = new ArchiveClock();
        var admin = new CollectionAdministration(f.Factory, clock);
        var gateway = new CollectorGateway(f.Factory, clock);
        Guid archive = await admin.CreateGroupAsync(f.Owner, "Архивная зона", 0, "archive", default);
        Guid active = await admin.CreateGroupAsync(f.Owner, "Действующая зона", 1, "archive", default);
        await Create("Архив ручной", archive, false); await Create("Архив расписание", archive, true);
        await Create("Архив повтор", archive, false); await Create("Действующий", active, true); await Create("Без зоны", null, true);
        var searches = (await admin.ReadAsync(f.Owner, default)).Searches.ToDictionary(s => s.Label);
        var agent = await admin.CreateAgentAsync(f.Owner, "Parser archive guard", true, "archive", default);
        await gateway.RegisterAsync(agent, new(1, "archive-test", [ListingSource.Avito]), default);
        Guid pending = Guid.CreateVersion7(), expired = Guid.CreateVersion7(), failed = Guid.CreateVersion7();
        await using (var db = f.Sandbox.Context())
        {
            // Deliberately inconsistent old data: archive must win even if Enabled stayed true.
            (await db.SearchGroups.SingleAsync(g => g.Id == archive)).Active = false;
            foreach (var s in await db.SearchConfigurations.Where(s => s.ScheduleKind == CollectionScheduleKind.Interval).ToArrayAsync())
                s.NextRunAt = clock.GetUtcNow().AddMinutes(-1);
            db.CollectionJobs.Add(new() { Id = pending, OrganizationId = f.OrganizationId, SearchId = searches["Архив ручной"].Id,
                CreatedAt = clock.GetUtcNow().AddDays(-2), State = CollectionJobState.Pending });
            db.CollectionJobs.Add(new() { Id = expired, OrganizationId = f.OrganizationId, SearchId = searches["Архив расписание"].Id,
                CreatedAt = clock.GetUtcNow().AddDays(-2), State = CollectionJobState.Leased, AgentId = agent.AgentId,
                LeaseId = Guid.CreateVersion7(), LeaseExpiresAt = clock.GetUtcNow().AddMinutes(-1) });
            db.CollectionJobs.Add(new() { Id = failed, OrganizationId = f.OrganizationId, SearchId = searches["Архив повтор"].Id,
                CreatedAt = clock.GetUtcNow().AddDays(-3), CompletedAt = clock.GetUtcNow().AddDays(-3).AddMinutes(1),
                State = CollectionJobState.Failed, ResultCode = "SourceError", ReasonCode = "NETWORK_TIMEOUT", RetryAt = clock.GetUtcNow().AddMinutes(-1) });
            await db.SaveChangesAsync();
        }
        var denied = await Assert.ThrowsExactlyAsync<ArgumentException>(() => admin.EnqueueAsync(f.Owner, searches["Архив ручной"].Id, "archive", default));
        StringAssert.Contains(denied.Message, "архиве");
        var protocol = await Assert.ThrowsExactlyAsync<CollectorProtocolException>(() => gateway.EnqueueSearchAsync(agent, new(searches["Архив ручной"].Id), default));
        Assert.AreEqual("SEARCH_GROUP_ARCHIVED", protocol.Code);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => admin.EnqueueAsync(f.ForeignOwner, searches["Архив ручной"].Id, "foreign", default));
        var scheduler = new CollectionScheduler(f.Factory, clock);
        Assert.AreEqual(2, await scheduler.RunDueAsync(default));
        Assert.AreEqual(0, await scheduler.RunDueAsync(default), "Archived overdue rows must not create work or spin the scheduler.");
        var seen = new HashSet<string>();
        for (int i = 0; i < 2; i++)
        {
            var work = await gateway.ClaimAsync(agent, default); Assert.IsNotNull(work);
            seen.Add(work.Label);
            await gateway.AcceptAsync(agent, new(Guid.CreateVersion7(), work.JobId, work.LeaseId, CollectionOutcome.Success, [], true), default);
        }
        CollectionAssert.AreEquivalent(ExpectedActiveLabels, seen.ToArray());
        Assert.IsNull(await gateway.ClaimAsync(agent, default), "Old pending/expired archive work is never assigned.");
        var history = await admin.ReadHistoryAsync(f.Owner, new(Text: "Архив"), default);
        Assert.AreEqual(3, history.Total);
        CollectionAssert.AreEquivalent(new[] { pending, expired, failed }, history.Items.Select(j => j.Id).ToArray());
        StringAssert.Contains(history.Items.Single(j => j.Id == failed).ReasonCode, "NETWORK_TIMEOUT");
        await using (var db = f.Sandbox.Context())
        {
            Assert.IsNull((await db.CollectionJobs.SingleAsync(j => j.Id == failed)).RetryAt);
            Assert.AreEqual(CollectionJobState.Pending, (await db.CollectionJobs.SingleAsync(j => j.Id == pending)).State);
            Assert.AreEqual(CollectionJobState.Leased, (await db.CollectionJobs.SingleAsync(j => j.Id == expired)).State);
            Assert.IsTrue((await db.SearchConfigurations.SingleAsync(s => s.Id == searches["Архив ручной"].Id)).Enabled);
        }
        await admin.EnqueueAsync(f.Owner, searches["Действующий"].Id, "active-manual", default);
        await gateway.EnqueueSearchAsync(agent, new(searches["Без зоны"].Id), default);
        Assert.AreEqual(3, (await admin.ReadHistoryAsync(f.Owner, new(Text: "Архив"), default)).Total);

        Task Create(string label, Guid? group, bool schedule) => admin.CreateSearchAsync(f.Owner,
            new(label, CatalogSource.Avito, "https://www.avito.ru/moskva/zemelnye_uchastki", 10, group,
                schedule ? new(CollectionScheduleKind.Interval, 5) : null), "archive", default);
    }

    [TestMethod]
    public async Task ArchivedLeaseIsNotRedeliveredButItsResultCanFinishAndForeignGroupsFailClosed()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        var clock = new ArchiveClock(); var admin = new CollectionAdministration(f.Factory, clock); var gateway = new CollectorGateway(f.Factory, clock);
        Guid group = await admin.CreateGroupAsync(f.Owner, "Позже в архив", 0, "archive", default);
        await admin.CreateSearchAsync(f.Owner, new("Уже выдан", CatalogSource.Avito, "https://www.avito.ru/moskva/zemelnye_uchastki", 10, group), "archive", default);
        Guid search = (await admin.ReadAsync(f.Owner, default)).Searches.Single().Id;
        var agent = await admin.CreateAgentAsync(f.Owner, "Parser existing lease", true, "archive", default);
        await gateway.RegisterAsync(agent, new(1, "archive-test", [ListingSource.Avito]), default);
        await admin.EnqueueAsync(f.Owner, search, "archive", default);
        var work = await gateway.ClaimAsync(agent, default); Assert.IsNotNull(work);
        await using (var db = f.Sandbox.Context()) { (await db.SearchGroups.SingleAsync(g => g.Id == group)).Active = false; await db.SaveChangesAsync(); }
        Assert.IsNull(await gateway.ClaimAsync(agent, default));
        await gateway.HeartbeatAsync(agent, new(work.JobId, work.LeaseId), default);
        await gateway.AcceptAsync(agent, new(Guid.CreateVersion7(), work.JobId, work.LeaseId, CollectionOutcome.Success, [], true), default);
        Assert.AreEqual("Completed", (await admin.ReadHistoryAsync(f.Owner, new(), default)).Items.Single().State);
        Assert.IsNull(await gateway.ClaimAsync(agent, default));

        Guid foreignGroup = await admin.CreateGroupAsync(f.ForeignOwner, "Foreign private group", 0, "foreign", default);
        await admin.CreateSearchAsync(f.Owner, new("Неверная группа", CatalogSource.Avito, "https://www.avito.ru/moskva/zemelnye_uchastki", 10,
            Schedule: new(CollectionScheduleKind.Interval, 5)), "archive", default);
        Guid foreignSearch = (await admin.ReadAsync(f.Owner, default)).Searches.Single(s => s.Label == "Неверная группа").Id;
        await using (var db = f.Sandbox.Context())
        {
            var row = await db.SearchConfigurations.SingleAsync(s => s.Id == foreignSearch);
            row.SearchGroupId = foreignGroup; row.NextRunAt = clock.GetUtcNow().AddMinutes(-1);
            db.CollectionJobs.Add(new() { Id = Guid.CreateVersion7(), OrganizationId = f.OrganizationId, SearchId = foreignSearch,
                State = CollectionJobState.Pending, CreatedAt = clock.GetUtcNow() });
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => admin.EnqueueAsync(f.Owner, foreignSearch, "foreign-group", default));
        await Assert.ThrowsExactlyAsync<CollectorProtocolException>(() => gateway.EnqueueSearchAsync(agent, new(foreignSearch), default));
        Assert.AreEqual(0, await new CollectionScheduler(f.Factory, clock).RunDueAsync(default));
        Assert.IsNull(await gateway.ClaimAsync(agent, default));
        Assert.AreEqual(2, (await admin.ReadHistoryAsync(f.Owner, new(), default)).Total);
    }
    private sealed class ArchiveClock : TimeProvider
    {
        private readonly DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
    }
}
