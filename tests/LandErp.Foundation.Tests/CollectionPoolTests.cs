using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.Collection.Contracts;
using LandErp.Application.Modules.Collection.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Collector.Contracts.V1;
using LandErp.Infrastructure.Modules.Collection;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
[TestCategory("PostgreSQL")]
public sealed class CollectionPoolTests
{
    [TestMethod]
    public async Task StaleAgentAcceptIsRejectedAfterExpiredLeaseIsReclaimed()
    {
        await using PostgresSandbox sandbox = await PostgresSandbox.CreateAsync();
        await using (LandErpDbContext migrator = sandbox.Context()) await migrator.Database.MigrateAsync();
        await using ServiceProvider bootstrap = IdentityOrganizationTests.Services(sandbox.MigratorConnection);
        Guid ownerId = await IdentityOrganizationTests.BootstrapAsync(bootstrap, "owner-stale-accept@test.invalid", "Stale accept");
        await IdentityOrganizationTests.EnableMfaAsync(bootstrap, ownerId);
        await sandbox.GrantRuntimeAsync();
        await using ServiceProvider services = IdentityOrganizationTests.Services(sandbox.RuntimeConnection);
        IDbContextFactory<LandErpDbContext> factory = services.GetRequiredService<IDbContextFactory<LandErpDbContext>>();
        TestClock clock = new();
        CollectionAdministration administration = new(factory, services.GetRequiredService<IAccessControl>(), clock);
        Subject owner = new(ownerId, true);
        AgentCredential agentA = await CreateRegisteredAsync(administration, factory, clock, owner, "Agent A", ListingSource.Avito);
        AgentCredential agentB = await CreateRegisteredAsync(administration, factory, clock, owner, "Agent B", ListingSource.Avito);
        await administration.CreateSearchAsync(owner, new("Lease fencing", CatalogSource.Avito,
            "https://www.avito.ru/moskva/zemelnye_uchastki", 1), "stale", CancellationToken.None);
        Guid searchId = (await administration.ReadAsync(owner, CancellationToken.None)).Searches.Single().Id;
        await administration.EnqueueAsync(owner, searchId, "stale", CancellationToken.None);
        CollectorGateway gatewayA = new(factory, clock); CollectorGateway gatewayB = new(factory, clock);

        CollectionWork firstLease = (await gatewayA.ClaimAsync(agentA, CancellationToken.None))!;
        clock.Advance(TimeSpan.FromMinutes(4));
        CollectionWork reclaimedLease = (await gatewayB.ClaimAsync(agentB, CancellationToken.None))!;
        Assert.AreEqual(firstLease.JobId, reclaimedLease.JobId);
        Assert.AreNotEqual(firstLease.LeaseId, reclaimedLease.LeaseId);
        await Assert.ThrowsExactlyAsync<CollectorProtocolException>(() => gatewayA.AcceptAsync(agentA,
            new(Guid.CreateVersion7(), firstLease.JobId, firstLease.LeaseId, CollectionOutcome.Success, [], true), CancellationToken.None));
        await gatewayB.AcceptAsync(agentB,
            new(Guid.CreateVersion7(), reclaimedLease.JobId, reclaimedLease.LeaseId, CollectionOutcome.Success, [], true), CancellationToken.None);
    }

    [TestMethod]
    public async Task SharedPoolClaimsByCapabilityFencesLeaseAndKeepsActualExecutor()
    {
        await using PostgresSandbox sandbox = await PostgresSandbox.CreateAsync();
        await using (LandErpDbContext migrator = sandbox.Context())
        {
            await migrator.Database.MigrateAsync();
            Assert.IsFalse(migrator.Database.HasPendingModelChanges());
        }

        await using ServiceProvider bootstrap = IdentityOrganizationTests.Services(sandbox.MigratorConnection);
        Guid ownerId = await IdentityOrganizationTests.BootstrapAsync(bootstrap, "owner-pool@test.invalid", "Shared pool");
        await IdentityOrganizationTests.EnableMfaAsync(bootstrap, ownerId);
        await sandbox.GrantRuntimeAsync();
        await using ServiceProvider services = IdentityOrganizationTests.Services(sandbox.RuntimeConnection);
        IDbContextFactory<LandErpDbContext> factory = services.GetRequiredService<IDbContextFactory<LandErpDbContext>>();
        IAccessControl access = services.GetRequiredService<IAccessControl>();
        TestClock clock = new();
        CollectionAdministration administration = new(factory, access, clock);
        Subject owner = new(ownerId, true);

        AgentCredential avitoA = await CreateRegisteredAsync(administration, factory, clock, owner, "Avito A", ListingSource.Avito);
        AgentCredential avitoB = await CreateRegisteredAsync(administration, factory, clock, owner, "Avito B", ListingSource.Avito);
        AgentCredential cian = await CreateRegisteredAsync(administration, factory, clock, owner, "Cian", ListingSource.Cian);
        await administration.CreateSearchAsync(owner,
            new("Общий Avito", CatalogSource.Avito, "https://www.avito.ru/moskva/zemelnye_uchastki", 2),
            "pool", CancellationToken.None);
        Guid searchId = (await administration.ReadAsync(owner, CancellationToken.None)).Searches.Single().Id;

        await administration.EnqueueAsync(owner, searchId, "pool", CancellationToken.None);
        await using (LandErpDbContext db = await factory.CreateDbContextAsync())
        {
            ServerCollectionJob pending = await db.CollectionJobs.AsNoTracking().SingleAsync();
            Assert.AreEqual(CollectionJobState.Pending, pending.State);
            Assert.IsNull(pending.AgentId, "Pending shared work must not have a preassigned executor.");
        }

        CollectorGateway gatewayCian = new(factory, clock);
        CollectorGateway gatewayA = new(factory, clock);
        CollectorGateway gatewayB = new(factory, clock);
        Assert.IsNull(await gatewayCian.ClaimAsync(cian, CancellationToken.None), "An incompatible Agent must not claim Avito work.");
        CollectionWork active = (await gatewayA.ClaimAsync(avitoA, CancellationToken.None))!;
        Assert.IsNull(await gatewayB.ClaimAsync(avitoB, CancellationToken.None), "An active lease must not be stolen.");

        clock.Advance(TimeSpan.FromMinutes(4));
        CollectionWork reclaimed = (await gatewayB.ClaimAsync(avitoB, CancellationToken.None))!;
        Assert.AreEqual(active.JobId, reclaimed.JobId);
        Assert.AreNotEqual(active.LeaseId, reclaimed.LeaseId);
        await Assert.ThrowsExactlyAsync<CollectorProtocolException>(() => gatewayA.HeartbeatAsync(avitoA,
            new(active.JobId, active.LeaseId), CancellationToken.None));
        await Assert.ThrowsExactlyAsync<CollectorProtocolException>(() => gatewayA.AcceptAsync(avitoA,
            new(Guid.CreateVersion7(), active.JobId, active.LeaseId, CollectionOutcome.Success, [], true), CancellationToken.None));
        await gatewayB.AcceptAsync(avitoB,
            new(Guid.CreateVersion7(), reclaimed.JobId, reclaimed.LeaseId, CollectionOutcome.Success, [], true), CancellationToken.None);

        await using (LandErpDbContext db = await factory.CreateDbContextAsync())
        {
            ServerCollectionJob completed = await db.CollectionJobs.AsNoTracking().SingleAsync();
            Assert.AreEqual(CollectionJobState.Completed, completed.State);
            Assert.AreEqual(avitoB.AgentId, completed.AgentId, "Terminal history must retain the actual executor.");
        }

        await administration.EnqueueAsync(owner, searchId, "pool-concurrent", CancellationToken.None);
        CollectionWork?[] claims = await Task.WhenAll(
            gatewayA.ClaimAsync(avitoA, CancellationToken.None),
            gatewayB.ClaimAsync(avitoB, CancellationToken.None));
        Assert.AreEqual(1, claims.Count(item => item != null));
        CollectionWork winner = claims.Single(item => item != null)!;
        AgentCredential winnerCredential = claims[0] != null ? avitoA : avitoB;
        CollectorGateway winnerGateway = claims[0] != null ? gatewayA : gatewayB;
        ListingData observation = new()
        {
            Source = ListingSource.Avito,
            ExternalId = "20001",
            Url = "https://www.avito.ru/moskva/zemelnye_uchastki/20001",
            ObservedAt = clock.GetUtcNow(),
            AdapterVersion = "v1-test",
            Provenance = "Phase 2A test",
            Title = new(FieldPresence.Present, "Общий пул")
        };
        await winnerGateway.AcceptAsync(winnerCredential,
            new(Guid.CreateVersion7(), winner.JobId, winner.LeaseId, CollectionOutcome.Success,
                [new("shared-pool-observation", observation)], true), CancellationToken.None);

        await using (LandErpDbContext db = await factory.CreateDbContextAsync())
        {
            Listing listing = await db.Listings.AsNoTracking().SingleAsync();
            Assert.IsNull(listing.DepartmentId);
            Assert.IsNull(listing.TeamId);
            Assert.AreEqual(CatalogSource.Avito, listing.Source);
            Assert.AreEqual(2, await db.CollectionJobs.CountAsync());
            Assert.AreEqual(2, await db.CollectionJobs.Where(item => item.State == CollectionJobState.Completed && item.AgentId != null).CountAsync());
            ServerCollectionJob counted = await db.CollectionJobs.SingleAsync(item => item.Id == winner.JobId);
            Assert.AreEqual(1, counted.ProcessedCount);
            Assert.AreEqual(1, counted.NewListingsCount);
            Assert.AreEqual(0, counted.ChangedListingsCount);
        }
    }

    [TestMethod]
    public async Task ParserSearchManagementRequiresPermissionIsIdempotentAndClaimResumesCurrentLease()
    {
        await using PostgresSandbox sandbox = await PostgresSandbox.CreateAsync();
        await using (LandErpDbContext migrator = sandbox.Context()) await migrator.Database.MigrateAsync();
        await using ServiceProvider bootstrap = IdentityOrganizationTests.Services(sandbox.MigratorConnection);
        Guid ownerId = await IdentityOrganizationTests.BootstrapAsync(bootstrap, "owner-parser-workspace@test.invalid", "Parser workspace");
        await IdentityOrganizationTests.EnableMfaAsync(bootstrap, ownerId);
        await sandbox.GrantRuntimeAsync();
        await using ServiceProvider services = IdentityOrganizationTests.Services(sandbox.RuntimeConnection);
        IDbContextFactory<LandErpDbContext> factory = services.GetRequiredService<IDbContextFactory<LandErpDbContext>>();
        TestClock clock = new();
        CollectionAdministration administration = new(factory, services.GetRequiredService<IAccessControl>(), clock);
        Subject owner = new(ownerId, true);
        AgentCredential restricted = await administration.CreateAgentAsync(owner, "Restricted parser", false, "parser-workspace", CancellationToken.None);
        AgentCredential manager = await administration.CreateAgentAsync(owner, "Managing parser", true, "parser-workspace", CancellationToken.None);
        CollectorGateway gateway = new(factory, clock);
        await gateway.RegisterAsync(restricted, new(1, "universal", [ListingSource.Avito]), CancellationToken.None);
        await gateway.RegisterAsync(manager, new(1, "universal", [ListingSource.Avito]), CancellationToken.None);

        CollectorProtocolException denied = await Assert.ThrowsExactlyAsync<CollectorProtocolException>(() =>
            gateway.ReadWorkspaceAsync(restricted, CancellationToken.None));
        Assert.AreEqual(CollectorErrorCodes.SearchPermissionRequired, denied.Code);

        Guid groupCommand = Guid.CreateVersion7();
        CollectorGroupView group = await gateway.CreateGroupAsync(manager,
            new(groupCommand, "Из Parser", 30), CancellationToken.None);
        CollectorGroupView replayedGroup = await gateway.CreateGroupAsync(manager,
            new(groupCommand, "Из Parser", 30), CancellationToken.None);
        Assert.AreEqual(group.Id, replayedGroup.Id);

        Guid searchCommand = Guid.CreateVersion7();
        CreateCollectorSearch createSearch = new(searchCommand, "Карта из Parser", ListingSource.Avito,
            "https://www.avito.ru/moskva/zemelnye_uchastki", 3, group.Id,
            new(CollectorScheduleKind.Manual));
        CollectorSearchView search = await gateway.CreateSearchAsync(manager, createSearch, CancellationToken.None);
        CollectorSearchView replayedSearch = await gateway.CreateSearchAsync(manager, createSearch, CancellationToken.None);
        Assert.AreEqual(search.Id, replayedSearch.Id);
        CollectorWorkspace workspace = await gateway.ReadWorkspaceAsync(manager, CancellationToken.None);
        Assert.AreEqual(1, workspace.Groups.Length);
        Assert.AreEqual(1, workspace.Searches.Length);

        await administration.EnqueueAsync(owner, search.Id, "parser-workspace", CancellationToken.None);
        CollectionWork firstClaim = (await gateway.ClaimAsync(manager, CancellationToken.None))!;
        CollectionWork resumedClaim = (await gateway.ClaimAsync(manager, CancellationToken.None))!;
        Assert.AreEqual(firstClaim.JobId, resumedClaim.JobId);
        Assert.AreEqual(firstClaim.LeaseId, resumedClaim.LeaseId);

        await gateway.HeartbeatAsync(manager, new(firstClaim.JobId, firstClaim.LeaseId,
            RuntimeState: AgentRuntimeState.AwaitingManualAction, SourceState: SourceRuntimeState.Captcha), CancellationToken.None);
        await gateway.HeartbeatAsync(manager, new(firstClaim.JobId, firstClaim.LeaseId,
            RuntimeState: AgentRuntimeState.Parsing, SourceState: SourceRuntimeState.Ready), CancellationToken.None);

        await using LandErpDbContext db = await factory.CreateDbContextAsync();
        Assert.AreEqual(SourceRuntimeState.Ready.ToString(),
            await db.CollectionJobs.Where(item => item.Id == firstClaim.JobId).Select(item => item.ResultCode).SingleAsync());
        Assert.AreEqual(1, await db.AuditEvents.CountAsync(item => item.Action == "CollectionSearchGroupCreatedByParser"));
        Assert.AreEqual(1, await db.AuditEvents.CountAsync(item => item.Action == "CollectionSearchCreatedByParser"));
    }

    private static async Task<AgentCredential> CreateRegisteredAsync(CollectionAdministration administration,
        IDbContextFactory<LandErpDbContext> factory, TimeProvider clock, Subject owner, string name, ListingSource source)
    {
        AgentCredential credential = await administration.CreateAgentAsync(owner, name, false, "pool", CancellationToken.None);
        CollectorGateway gateway = new(factory, clock);
        await gateway.RegisterAsync(credential, new(1, "phase2a", [source]), CancellationToken.None);
        return credential;
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan interval) => now += interval;
    }
}
