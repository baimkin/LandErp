using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.Collection.Contracts;
using LandErp.Application.Modules.Collection.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Infrastructure.Modules.Collection;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
[TestCategory("PostgreSQL")]
public sealed class CollectionSchedulingTests
{
    [TestMethod]
    public async Task GroupsTypedSchedulesAndConcurrentTicksUseSharedPoolIdempotently()
    {
        await using PostgresSandbox sandbox = await PostgresSandbox.CreateAsync();
        await using (LandErpDbContext migrator = sandbox.Context())
        {
            await migrator.Database.MigrateAsync();
            Assert.IsFalse(migrator.Database.HasPendingModelChanges());
        }
        await using ServiceProvider bootstrap = IdentityOrganizationTests.Services(sandbox.MigratorConnection);
        Guid ownerId = await IdentityOrganizationTests.BootstrapAsync(bootstrap, "owner-schedule@test.invalid", "Schedules");
        Guid foreignId = await IdentityOrganizationTests.BootstrapAsync(bootstrap, "foreign-schedule@test.invalid", "Foreign");
        await IdentityOrganizationTests.EnableMfaAsync(bootstrap, ownerId);
        await IdentityOrganizationTests.EnableMfaAsync(bootstrap, foreignId);
        await sandbox.GrantRuntimeAsync();
        await using ServiceProvider services = IdentityOrganizationTests.Services(sandbox.RuntimeConnection);
        var factory = services.GetRequiredService<IDbContextFactory<LandErpDbContext>>();
        var access = services.GetRequiredService<IAccessControl>();
        Clock clock = new();
        CollectionAdministration admin = new(factory, access, clock);
        Subject owner = new(ownerId, true);
        Guid groupId = await admin.CreateGroupAsync(owner, "Московская область", 10, "schedule", CancellationToken.None);
        await admin.CreateSearchAsync(owner, new("Интервальный Avito", CatalogSource.Avito,
            "https://www.avito.ru/moskva/zemelnye_uchastki", 3, groupId, new(CollectionScheduleKind.Interval, 5)), "schedule", CancellationToken.None);
        await admin.CreateSearchAsync(owner, new("Утренний Cian", CatalogSource.Cian,
            "https://www.cian.ru/cat.php", 2, groupId, new(CollectionScheduleKind.FixedTimes, FixedTimes: ["09:00", "18:30"])), "schedule", CancellationToken.None);
        CollectionAdminView view = await admin.ReadAsync(owner, CancellationToken.None);
        Assert.AreEqual(1, view.Groups.Count);
        Assert.AreEqual(2, view.Groups[0].SearchCount);
        Assert.IsTrue(view.Searches.Any(item => item.Schedule.Contains('5')));
        Assert.IsTrue(view.Searches.Any(item => item.Schedule.Contains("09:00", StringComparison.Ordinal)));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => admin.ArchiveGroupAsync(new(foreignId, true), groupId, 1, "foreign", CancellationToken.None));

        clock.Advance(TimeSpan.FromMinutes(5));
        CollectionScheduler first = new(factory, clock); CollectionScheduler second = new(factory, clock);
        int[] created = await Task.WhenAll(first.RunDueAsync(CancellationToken.None), second.RunDueAsync(CancellationToken.None));
        Assert.AreEqual(1, created.Sum());
        await using LandErpDbContext db = await factory.CreateDbContextAsync();
        ServerCollectionJob job = await db.CollectionJobs.AsNoTracking().SingleAsync();
        Assert.AreEqual(CollectionJobState.Pending, job.State);
        Assert.IsNull(job.AgentId);
        Assert.IsNotNull(job.ScheduledFor);
        Assert.AreEqual(1, await db.CollectionJobs.CountAsync());
        SearchConfiguration interval = await db.SearchConfigurations.SingleAsync(item => item.Source == CatalogSource.Avito);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => admin.EnqueueAsync(owner, interval.Id, "duplicate", CancellationToken.None));
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 9, 15, 8, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan value) => now += value;
    }
}
