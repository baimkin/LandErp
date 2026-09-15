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
        }
    }

    private static async Task<AgentCredential> CreateRegisteredAsync(CollectionAdministration administration,
        IDbContextFactory<LandErpDbContext> factory, TimeProvider clock, Subject owner, string name, ListingSource source)
    {
        AgentCredential credential = await administration.CreateAgentAsync(owner, name, "pool", CancellationToken.None);
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
