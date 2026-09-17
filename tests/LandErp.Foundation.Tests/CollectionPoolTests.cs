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
    public async Task ActivationProgressAttentionAndLeaseCodesAreExplicit()
    {
        await using PostgresSandbox sandbox = await PostgresSandbox.CreateAsync();
        await using (LandErpDbContext migrator = sandbox.Context()) await migrator.Database.MigrateAsync();
        await using ServiceProvider bootstrap = IdentityOrganizationTests.Services(sandbox.MigratorConnection);
        Guid ownerId = await IdentityOrganizationTests.BootstrapAsync(bootstrap, "owner-machine-protocol@test.invalid", "Machine protocol");
        await IdentityOrganizationTests.EnableMfaAsync(bootstrap, ownerId);
        await sandbox.GrantRuntimeAsync();
        await using ServiceProvider services = IdentityOrganizationTests.Services(sandbox.RuntimeConnection);
        IDbContextFactory<LandErpDbContext> factory = services.GetRequiredService<IDbContextFactory<LandErpDbContext>>();
        TestClock clock = new(); Subject owner = new(ownerId, true);
        CollectionAdministration administration = new(factory, services.GetRequiredService<IAccessControl>(), clock);
        AgentConnectionCode code = await administration.CreateConnectionCodeAsync(owner, "Parser PC", "activation", CancellationToken.None);
        CollectorGateway gatewayA = new(factory, clock);
        CollectorProtocolException invalidActivation = await Assert.ThrowsExactlyAsync<CollectorProtocolException>(() =>
            gatewayA.ActivateAsync(new(code.AgentId, null!, "PC-01", 1, "protocol-test", [ListingSource.Avito]), CancellationToken.None));
        Assert.AreEqual("ACTIVATION_INVALID", invalidActivation.Code);
        AgentActivation activation = new(code.AgentId, code.ActivationSecret, "PC-01", 1, "protocol-test", [ListingSource.Avito]);
        AgentActivationReceipt activated = await gatewayA.ActivateAsync(activation, CancellationToken.None);
        AgentCredential agentA = new(activated.AgentId, activated.Credential);
        CollectorProtocolException reused = await Assert.ThrowsExactlyAsync<CollectorProtocolException>(() =>
            gatewayA.ActivateAsync(activation, CancellationToken.None));
        Assert.AreEqual("ACTIVATION_USED", reused.Code);

        AgentCredential agentB = await CreateRegisteredAsync(administration, factory, clock, owner, "Replacement", ListingSource.Avito);
        await administration.CreateSearchAsync(owner, new("Protocol", CatalogSource.Avito,
            "https://www.avito.ru/moskva/zemelnye_uchastki", 5), "protocol", CancellationToken.None);
        Guid searchId = (await administration.ReadAsync(owner, CancellationToken.None)).Searches.Single().Id;
        await administration.EnqueueAsync(owner, searchId, "protocol", CancellationToken.None);
        CollectionWork first = (await gatewayA.ClaimAsync(agentA, CancellationToken.None))!;
        await gatewayA.HeartbeatAsync(agentA, new(first.JobId, first.LeaseId, CollectionOutcome.Captcha,
            RuntimeState: AgentRuntimeState.AwaitingManualAction, SourceState: SourceRuntimeState.Captcha,
            Progress: new(2, 5, 5, 20, CollectionProgressPhase.WaitingForUser, clock.GetUtcNow())), CancellationToken.None);
        AgentView attention = (await administration.ReadAsync(owner, CancellationToken.None)).Agents.Single(item => item.Id == agentA.AgentId);
        Assert.AreEqual("CAPTCHA", attention.OperationalStatus);
        Assert.AreEqual(5, attention.ProgressProcessed);
        Assert.AreEqual(20, attention.ProgressTotal);
        await gatewayA.HeartbeatAsync(agentA, new(first.JobId, first.LeaseId,
            RuntimeState: AgentRuntimeState.Parsing, SourceState: SourceRuntimeState.Ready,
            Progress: new(2, 5, 6, 20, CollectionProgressPhase.ReadingPage, clock.GetUtcNow())), CancellationToken.None);
        AgentView resumed = (await administration.ReadAsync(owner, CancellationToken.None)).Agents.Single(item => item.Id == agentA.AgentId);
        Assert.AreEqual("Выполняет сбор", resumed.OperationalStatus);
        Assert.AreEqual("", resumed.AttentionCode);

        clock.Advance(TimeSpan.FromMinutes(4));
        CollectorProtocolException expired = await Assert.ThrowsExactlyAsync<CollectorProtocolException>(() =>
            gatewayA.HeartbeatAsync(agentA, new(first.JobId, first.LeaseId), CancellationToken.None));
        Assert.AreEqual("LEASE_EXPIRED", expired.Code);
        CollectorGateway gatewayB = new(factory, clock);
        CollectionWork replacement = (await gatewayB.ClaimAsync(agentB, CancellationToken.None))!;
        CollectorProtocolException replaced = await Assert.ThrowsExactlyAsync<CollectorProtocolException>(() =>
            gatewayA.HeartbeatAsync(agentA, new(first.JobId, first.LeaseId), CancellationToken.None));
        Assert.AreEqual("LEASE_REPLACED", replaced.Code);
        await gatewayB.AcceptAsync(agentB, new(Guid.CreateVersion7(), replacement.JobId, replacement.LeaseId,
            CollectionOutcome.Success, [], true), CancellationToken.None);
        CollectorProtocolException superseded = await Assert.ThrowsExactlyAsync<CollectorProtocolException>(() =>
            gatewayA.AcceptAsync(agentA, new(Guid.CreateVersion7(), first.JobId, first.LeaseId,
                CollectionOutcome.Success, [], true), CancellationToken.None));
        Assert.AreEqual("RESULT_SUPERSEDED", superseded.Code);
    }

    [TestMethod]
    public async Task RepeatedClaimReturnsTheSingleActiveLeaseAndLeavesOtherWorkPending()
    {
        await using PostgresSandbox sandbox = await PostgresSandbox.CreateAsync();
        await using (LandErpDbContext migrator = sandbox.Context()) await migrator.Database.MigrateAsync();
        await using ServiceProvider bootstrap = IdentityOrganizationTests.Services(sandbox.MigratorConnection);
        Guid ownerId = await IdentityOrganizationTests.BootstrapAsync(bootstrap, "owner-single-agent-work@test.invalid", "Single agent work");
        await IdentityOrganizationTests.EnableMfaAsync(bootstrap, ownerId);
        await sandbox.GrantRuntimeAsync();
        await using ServiceProvider services = IdentityOrganizationTests.Services(sandbox.RuntimeConnection);
        IDbContextFactory<LandErpDbContext> factory = services.GetRequiredService<IDbContextFactory<LandErpDbContext>>();
        TestClock clock = new();
        CollectionAdministration administration = new(factory, services.GetRequiredService<IAccessControl>(), clock);
        Subject owner = new(ownerId, true);
        AgentCredential agent = await CreateRegisteredAsync(administration, factory, clock, owner, "One worker", ListingSource.Avito);
        await administration.CreateSearchAsync(owner, new("First", CatalogSource.Avito,
            "https://www.avito.ru/moskva/zemelnye_uchastki", 1), "single", CancellationToken.None);
        await administration.CreateSearchAsync(owner, new("Second", CatalogSource.Avito,
            "https://www.avito.ru/moskva/zemelnye_uchastki", 1), "single", CancellationToken.None);
        foreach (SearchView search in (await administration.ReadAsync(owner, CancellationToken.None)).Searches)
            await administration.EnqueueAsync(owner, search.Id, "single", CancellationToken.None);

        CollectorGateway gateway = new(factory, clock);
        CollectionWork first = (await gateway.ClaimAsync(agent, CancellationToken.None))!;
        CollectionWork repeated = (await gateway.ClaimAsync(agent, CancellationToken.None))!;
        Assert.AreEqual(first.JobId, repeated.JobId);
        Assert.AreEqual(first.LeaseId, repeated.LeaseId);
        await using LandErpDbContext db = await factory.CreateDbContextAsync();
        Assert.AreEqual(1, await db.CollectionJobs.CountAsync(item => item.State == CollectionJobState.Leased));
        Assert.AreEqual(1, await db.CollectionJobs.CountAsync(item => item.State == CollectionJobState.Pending));
    }

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
        Assert.AreEqual(CollectorErrorCodes.IdempotencyConflict, (await Assert.ThrowsExactlyAsync<CollectorProtocolException>(() =>
            gateway.CreateSearchAsync(manager, createSearch with { MaxPages = 9 }, CancellationToken.None))).Code);
        CollectorWorkspace workspace = await gateway.ReadWorkspaceAsync(manager, CancellationToken.None);
        Assert.AreEqual(1, workspace.Groups.Length);
        Assert.AreEqual(1, workspace.Searches.Length);

        UpdateCollectorSearch edit = new(search.Id, search.Revision, "Изменён из Parser", search.Source, search.Url, 7,
            group.Id, new(CollectorScheduleKind.Interval, 60), true);
        Assert.AreEqual(CollectorErrorCodes.SearchPermissionRequired,
            (await Assert.ThrowsExactlyAsync<CollectorProtocolException>(() => gateway.UpdateSearchAsync(restricted, edit, CancellationToken.None))).Code);
        Assert.AreEqual(CollectorErrorCodes.SearchPermissionRequired,
            (await Assert.ThrowsExactlyAsync<CollectorProtocolException>(() => gateway.UpdateGroupAsync(restricted, new(group.Id, group.Revision, group.Name, 30, true), CancellationToken.None))).Code);
        Assert.AreEqual(CollectorErrorCodes.SearchPermissionRequired,
            (await Assert.ThrowsExactlyAsync<CollectorProtocolException>(() => gateway.EnqueueSearchAsync(restricted, new(search.Id), CancellationToken.None))).Code);
        search = await gateway.UpdateSearchAsync(manager, edit, CancellationToken.None);
        Assert.AreEqual(7, search.MaxPages); Assert.AreEqual(60, search.Schedule.IntervalMinutes);
        await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(() => gateway.UpdateSearchAsync(manager, edit, CancellationToken.None));
        group = await gateway.UpdateGroupAsync(manager, new(group.Id, group.Revision, "Переименована", 30, true), CancellationToken.None);
        Assert.AreEqual("Переименована", group.Name);
        Assert.AreEqual("GROUP_HAS_ACTIVE_SEARCHES", (await Assert.ThrowsExactlyAsync<CollectorProtocolException>(() =>
            gateway.UpdateGroupAsync(manager, new(group.Id, group.Revision, group.Name, 30, false), CancellationToken.None))).Code);
        await gateway.EnqueueSearchAsync(manager, new(search.Id), CancellationToken.None);
        await gateway.EnqueueSearchAsync(manager, new(search.Id), CancellationToken.None);
        Assert.AreEqual("SEARCH_BUSY", (await Assert.ThrowsExactlyAsync<CollectorProtocolException>(() =>
            gateway.UpdateSearchAsync(manager, edit with { ExpectedRevision = search.Revision }, CancellationToken.None))).Code);
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
        Assert.AreEqual(1, await db.AuditEvents.CountAsync(item => item.Action == "CollectionSearchUpdatedByParser"));
        Assert.AreEqual(1, await db.AuditEvents.CountAsync(item => item.Action == "CollectionSearchGroupUpdatedByParser"));
        Assert.AreEqual(1, await db.AuditEvents.CountAsync(item => item.Action == "CollectionJobQueuedByParser"));
        Assert.AreEqual(1, await db.CollectionJobs.CountAsync(item => item.SearchId == search.Id));
    }

    [TestMethod]
    public async Task PartialResultsRetryBoundedlyAndLaterSuccessResetsAttention()
    {
        await using PostgresSandbox sandbox = await PostgresSandbox.CreateAsync();
        await using (LandErpDbContext migrator = sandbox.Context())
        {
            await migrator.Database.MigrateAsync();
            Assert.IsFalse(migrator.Database.HasPendingModelChanges());
        }
        await using ServiceProvider bootstrap = IdentityOrganizationTests.Services(sandbox.MigratorConnection);
        Guid ownerId = await IdentityOrganizationTests.BootstrapAsync(bootstrap, "owner-partial-collection@test.invalid", "Partial collection");
        await IdentityOrganizationTests.EnableMfaAsync(bootstrap, ownerId);
        await sandbox.GrantRuntimeAsync();
        await using ServiceProvider services = IdentityOrganizationTests.Services(sandbox.RuntimeConnection);
        IDbContextFactory<LandErpDbContext> factory = services.GetRequiredService<IDbContextFactory<LandErpDbContext>>();
        TestClock clock = new(); Subject owner = new(ownerId, true);
        CollectionAdministration administration = new(factory, services.GetRequiredService<IAccessControl>(), clock);
        AgentCredential agent = await CreateRegisteredAsync(administration, factory, clock, owner, "Recovery parser", ListingSource.Avito);
        await administration.CreateSearchAsync(owner, new("Recovery", CatalogSource.Avito,
            "https://www.avito.ru/moskva/zemelnye_uchastki", 10), "recovery", CancellationToken.None);
        Guid searchId = (await administration.ReadAsync(owner, CancellationToken.None)).Searches.Single().Id;
        await administration.EnqueueAsync(owner, searchId, "recovery", CancellationToken.None);
        CollectorGateway gateway = new(factory, clock); CollectionScheduler scheduler = new(factory, clock);
        TimeSpan[] delays = [TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(45)];

        for (int attempt = 0; attempt <= delays.Length; attempt++)
        {
            CollectionWork work = (await gateway.ClaimAsync(agent, CancellationToken.None))!;
            ObservationEnvelope[] observations = attempt == 0
                ? [new("partial-one", new ListingData
                {
                    Source = ListingSource.Avito, ExternalId = "10001",
                    Url = "https://www.avito.ru/moskva/zemelnye_uchastki/partial_10001",
                    ObservedAt = clock.GetUtcNow(), AdapterVersion = "test", Provenance = "Partial test",
                    Title = new(FieldPresence.Present, "Частично полученное объявление")
                })]
                : [];
            CollectionResult partial = new(Guid.CreateVersion7(), work.JobId, work.LeaseId, CollectionOutcome.Partial,
                observations, true, CollectionResultReasonCodes.LoadingInterrupted, [],
                new(1, 10, false, false, 0, 0, 10));
            await gateway.AcceptAsync(agent, partial, CancellationToken.None);

            await using (LandErpDbContext db = await factory.CreateDbContextAsync())
            {
                ServerCollectionJob saved = await db.CollectionJobs.AsNoTracking().SingleAsync(item => item.Id == work.JobId);
                Assert.AreEqual(CollectionJobState.Partial, saved.State);
                Assert.AreEqual(attempt, saved.RetryAttempt);
                Assert.AreEqual(attempt == delays.Length, saved.RequiresOperatorAttention);
                Assert.AreEqual(attempt < delays.Length ? clock.GetUtcNow().Add(delays[attempt]) : null, saved.RetryAt);
            }
            if (attempt == delays.Length) break;
            clock.Advance(delays[attempt]);
            Assert.AreEqual(1, await scheduler.RunDueAsync(CancellationToken.None));
        }

        CollectionAdminView failed = await administration.ReadAsync(owner, CancellationToken.None);
        Assert.AreEqual(1, failed.AttentionJobs);
        Assert.AreEqual(1, failed.Jobs.Sum(item => item.AcceptedCount));
        Assert.AreEqual(4, failed.Jobs.Count);
        Assert.AreEqual("", failed.Agents.Single().AttentionCode);
        Assert.AreEqual(AgentRuntimeState.Idle, failed.Agents.Single().RuntimeState);
        await using (LandErpDbContext failedDb = await factory.CreateDbContextAsync())
            Assert.AreEqual(4, (await failedDb.SearchConfigurations.SingleAsync()).ConsecutiveFailures);

        await administration.EnqueueAsync(owner, searchId, "manual-recovery", CancellationToken.None);
        CollectionWork recovery = (await gateway.ClaimAsync(agent, CancellationToken.None))!;
        CollectionResult invalid = new(Guid.CreateVersion7(), recovery.JobId, recovery.LeaseId, CollectionOutcome.Success, [], true,
            Coverage: new(38, 51, false, true, 3, 1, 10, 6));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => gateway.AcceptAsync(agent, invalid, CancellationToken.None));
        CollectionResult success = invalid with
        {
            ResultId = Guid.CreateVersion7(), ReasonCode = CollectionResultReasonCodes.CountHintMismatch,
            Warnings = [CollectionResultReasonCodes.CountHintMismatch], Coverage = invalid.Coverage! with { EndReached = true }
        };
        await gateway.AcceptAsync(agent, success, CancellationToken.None);

        CollectionAdminView recovered = await administration.ReadAsync(owner, CancellationToken.None);
        Assert.AreEqual(0, recovered.AttentionJobs);
        Assert.AreEqual("Completed", recovered.Searches.Single().LastRun!.State);
        Assert.AreEqual(38, recovered.Searches.Single().LastRun!.Coverage!.UniqueObserved);
        Assert.AreEqual(CollectionResultReasonCodes.CountHintMismatch, recovered.Searches.Single().LastRun!.Warnings.Single());
        await using LandErpDbContext finalDb = await factory.CreateDbContextAsync();
        Assert.AreEqual(0, (await finalDb.SearchConfigurations.SingleAsync()).ConsecutiveFailures);
        Assert.AreEqual(1, await finalDb.Listings.CountAsync());
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
