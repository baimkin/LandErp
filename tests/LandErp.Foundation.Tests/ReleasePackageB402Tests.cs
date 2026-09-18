using LandErp.Application.Foundation.Files;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.Collection.Contracts;
using LandErp.Application.Modules.Collection.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Collector.Contracts.V1;
using LandErp.Infrastructure.Foundation.Files;
using LandErp.Infrastructure.Modules.Collection;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
public sealed class ReleasePackageB402Tests
{
    [TestMethod]
    [TestCategory("PostgreSQL")]
    public async Task OperationalReadModelExposesExpiredLeaseAsReclaimableBacklog()
    {
        await using PostgresSandbox sandbox = await PostgresSandbox.CreateAsync();
        await using (LandErpDbContext migrator = sandbox.Context()) await migrator.Database.MigrateAsync();
        await using ServiceProvider bootstrap = IdentityOrganizationTests.Services(sandbox.MigratorConnection);
        Guid ownerId = await IdentityOrganizationTests.BootstrapAsync(bootstrap, "owner-b402-health@test.invalid", "B4-02 health");
        await IdentityOrganizationTests.EnableMfaAsync(bootstrap, ownerId);
        await sandbox.GrantRuntimeAsync();
        await using ServiceProvider services = IdentityOrganizationTests.Services(sandbox.RuntimeConnection);
        IDbContextFactory<LandErpDbContext> factory = services.GetRequiredService<IDbContextFactory<LandErpDbContext>>();
        TestClock clock = new();
        Subject owner = new(ownerId, true);
        CollectionAdministration administration = new(factory, services.GetRequiredService<IAccessControl>(), clock);
        AgentCredential agent = await administration.CreateAgentAsync(owner, "Health parser", false, "b402", CancellationToken.None);
        CollectorGateway gateway = new(factory, clock);
        await gateway.RegisterAsync(agent, new(1, "b402", [ListingSource.Avito]), CancellationToken.None);
        await administration.CreateSearchAsync(owner, new("Health search", CatalogSource.Avito,
            "https://www.avito.ru/moskva/zemelnye_uchastki", 1), "b402", CancellationToken.None);
        Guid searchId = (await administration.ReadAsync(owner, CancellationToken.None)).Searches.Single().Id;
        await administration.EnqueueAsync(owner, searchId, "b402", CancellationToken.None);
        _ = await gateway.ClaimAsync(agent, CancellationToken.None);

        CollectionAdminView active = await administration.ReadAsync(owner, CancellationToken.None);
        Assert.AreEqual(0, active.PendingJobs);
        Assert.AreEqual(0, active.ExpiredLeases);
        Assert.AreEqual(1, active.BusyAgents);

        clock.Advance(TimeSpan.FromMinutes(4));
        CollectionAdminView expired = await administration.ReadAsync(owner, CancellationToken.None);
        Assert.AreEqual(0, expired.PendingJobs);
        Assert.AreEqual(1, expired.ExpiredLeases);
        Assert.AreEqual(0, expired.BusyAgents);
    }

    [TestMethod]
    [TestCategory("FileStorage")]
    public async Task YandexOperationalProbeIsReadOnlyAndReportsProviderReachability()
    {
        using YandexDiskStorageTests.DiskHandler handler = new();
        using HttpClient http = new(handler);
        using YandexDiskFileStorage storage = new(YandexDiskStorageTests.Options(), http);

        FileStorageHealth health = await storage.CheckAsync(CancellationToken.None);

        Assert.IsTrue(health.Available);
        Assert.AreEqual("STORAGE_READY", health.Code);
        Assert.AreEqual(0, handler.UploadCount, "Health check must not create a probe file.");
        Assert.AreEqual(1, handler.RequestCount, "Health check should be one read-only provider metadata request.");
    }

    [TestMethod]
    [TestCategory("FileStorage")]
    public async Task YandexOperationalProbeReturnsSafeFailureCode()
    {
        using YandexDiskStorageTests.DiskHandler handler = new() { ApiFailure = System.Net.HttpStatusCode.Unauthorized };
        using HttpClient http = new(handler);
        using YandexDiskFileStorage storage = new(YandexDiskStorageTests.Options(), http);

        FileStorageHealth health = await storage.CheckAsync(CancellationToken.None);

        Assert.IsFalse(health.Available);
        Assert.AreEqual("STORAGE_AUTH", health.Code);
        Assert.AreEqual(0, handler.UploadCount);
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan interval) => now += interval;
    }
}
