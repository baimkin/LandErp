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
    public async Task ManualScheduleOnlyCreatesWorkWhenRunNowIsRequested()
    {
        await using SchedulingFixture fixture = await SchedulingFixture.CreateAsync(
            "manual-schedule@test.invalid", new(2026, 9, 15, 5, 0, 0, TimeSpan.Zero));
        await fixture.Admin.CreateSearchAsync(fixture.Owner, new("Ручной Avito", CatalogSource.Avito,
            "https://www.avito.ru/moskva/zemelnye_uchastki", 2, Schedule: new(CollectionScheduleKind.Manual)),
            "manual", CancellationToken.None);
        SearchView search = (await fixture.Admin.ReadAsync(fixture.Owner, CancellationToken.None)).Searches.Single();
        Assert.AreEqual(CollectionScheduleKind.Manual, search.ScheduleKind);
        Assert.IsNull(search.NextRunAt);

        fixture.Clock.Advance(TimeSpan.FromDays(1));
        Assert.AreEqual(0, await new CollectionScheduler(fixture.Factory, fixture.Clock).RunDueAsync(CancellationToken.None));
        await fixture.Admin.EnqueueAsync(fixture.Owner, search.Id, "manual-run", CancellationToken.None);
        await using LandErpDbContext db = await fixture.Factory.CreateDbContextAsync();
        ServerCollectionJob job = await db.CollectionJobs.AsNoTracking().SingleAsync();
        Assert.IsNull(job.ScheduledFor);
        Assert.AreEqual(CollectionJobState.Pending, job.State);
    }

    [TestMethod]
    public async Task IntervalScheduleCreatesOneJobAtEachDueBoundary()
    {
        DateTimeOffset start = new(2026, 9, 15, 5, 0, 0, TimeSpan.Zero);
        await using SchedulingFixture fixture = await SchedulingFixture.CreateAsync("interval-schedule@test.invalid", start);
        await fixture.Admin.CreateSearchAsync(fixture.Owner, new("Интервальный Avito", CatalogSource.Avito,
            "https://www.avito.ru/moskva/zemelnye_uchastki", 2, Schedule: new(CollectionScheduleKind.Interval, 5)),
            "interval", CancellationToken.None);
        SearchView search = (await fixture.Admin.ReadAsync(fixture.Owner, CancellationToken.None)).Searches.Single();
        Assert.AreEqual(start.AddMinutes(5), search.NextRunAt);

        CollectionScheduler scheduler = new(fixture.Factory, fixture.Clock);
        fixture.Clock.Advance(TimeSpan.FromMinutes(4));
        Assert.AreEqual(0, await scheduler.RunDueAsync(CancellationToken.None));
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.AreEqual(1, await scheduler.RunDueAsync(CancellationToken.None));
        await using LandErpDbContext db = await fixture.Factory.CreateDbContextAsync();
        Assert.AreEqual(start.AddMinutes(5), (await db.CollectionJobs.AsNoTracking().SingleAsync()).ScheduledFor);
        Assert.AreEqual(start.AddMinutes(10), (await db.SearchConfigurations.AsNoTracking().SingleAsync()).NextRunAt);
        CollectionAdminView view = await fixture.Admin.ReadAsync(fixture.Owner, CancellationToken.None);
        Assert.AreEqual("Работает", view.Scheduler.State);
        Assert.AreEqual(start.AddMinutes(5), view.Scheduler.LastSucceededAt);
        Assert.AreEqual(1, view.Scheduler.LastQueuedCount);
    }

    [TestMethod]
    public async Task FixedTimesScheduleUsesBusinessTimeAndAdvancesToTheNextSlot()
    {
        DateTimeOffset start = new(2026, 9, 15, 5, 30, 0, TimeSpan.Zero); // 08:30 Europe/Moscow.
        await using SchedulingFixture fixture = await SchedulingFixture.CreateAsync("fixed-schedule@test.invalid", start);
        await fixture.Admin.CreateSearchAsync(fixture.Owner, new("Фиксированный Cian", CatalogSource.Cian,
            "https://www.cian.ru/cat.php", 2, Schedule: new(CollectionScheduleKind.FixedTimes, FixedTimes: ["09:00", "18:30"])),
            "fixed", CancellationToken.None);
        SearchView search = (await fixture.Admin.ReadAsync(fixture.Owner, CancellationToken.None)).Searches.Single();
        Assert.AreEqual(new DateTimeOffset(2026, 9, 15, 6, 0, 0, TimeSpan.Zero), search.NextRunAt);

        CollectionScheduler scheduler = new(fixture.Factory, fixture.Clock);
        fixture.Clock.Advance(TimeSpan.FromMinutes(30));
        Assert.AreEqual(1, await scheduler.RunDueAsync(CancellationToken.None));
        await using (LandErpDbContext db = await fixture.Factory.CreateDbContextAsync())
        {
            Assert.AreEqual(new DateTimeOffset(2026, 9, 15, 15, 30, 0, TimeSpan.Zero),
                (await db.SearchConfigurations.AsNoTracking().SingleAsync()).NextRunAt);
            (await db.CollectionJobs.SingleAsync()).State = CollectionJobState.Interrupted;
            await db.SaveChangesAsync();
        }
        fixture.Clock.Advance(TimeSpan.FromHours(9.5));
        Assert.AreEqual(1, await scheduler.RunDueAsync(CancellationToken.None));
        await using LandErpDbContext finalDb = await fixture.Factory.CreateDbContextAsync();
        Assert.AreEqual(2, await finalDb.CollectionJobs.CountAsync());
        Assert.AreEqual(new DateTimeOffset(2026, 9, 16, 6, 0, 0, TimeSpan.Zero),
            (await finalDb.SearchConfigurations.AsNoTracking().SingleAsync()).NextRunAt);
    }

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
        Clock clock = new(new(2026, 9, 15, 8, 0, 0, TimeSpan.Zero));
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

    [TestMethod]
    public async Task ManagementReadUsesExactAggregatesStructuredLastRunAndSeparateReadPermission()
    {
        DateTimeOffset start = new(2026, 9, 15, 8, 0, 0, TimeSpan.Zero);
        await using SchedulingFixture fixture = await SchedulingFixture.CreateAsync("collection-read@test.invalid", start);
        await fixture.Admin.CreateSearchAsync(fixture.Owner, new("Read model", CatalogSource.Avito,
            "https://www.avito.ru/moskva/zemelnye_uchastki", 2), "read", CancellationToken.None);
        SearchView search = (await fixture.Admin.ReadAsync(fixture.Owner, CancellationToken.None)).Searches.Single();
        Guid organizationId;
        await using (LandErpDbContext db = await fixture.Factory.CreateDbContextAsync())
        {
            organizationId = (await db.SearchConfigurations.AsNoTracking().SingleAsync()).OrganizationId;
            db.CollectionJobs.Add(new()
            {
                Id = Guid.CreateVersion7(), OrganizationId = organizationId, SearchId = search.Id,
                State = CollectionJobState.Pending, CreatedAt = start.AddDays(-10)
            });
            db.CollectionJobs.Add(new()
            {
                Id = Guid.CreateVersion7(), OrganizationId = organizationId, SearchId = search.Id,
                State = CollectionJobState.Failed, ResultCode = "SourceError", CreatedAt = start.AddDays(-2), CompletedAt = start.AddDays(-2)
            });
            for (int index = 0; index < 101; index++)
            {
                db.CollectionJobs.Add(new()
                {
                    Id = Guid.CreateVersion7(), OrganizationId = organizationId, SearchId = search.Id,
                    State = CollectionJobState.Completed, ResultCode = "Success", CreatedAt = start.AddMinutes(index),
                    CompletedAt = start.AddMinutes(index), ProcessedCount = index, NewListingsCount = index == 100 ? 3 : 0,
                    ChangedListingsCount = index == 100 ? 2 : 0
                });
            }
            await db.SaveChangesAsync();
        }

        AccessContext organization = new(Guid.CreateVersion7(), organizationId, null, null, AccessScope.Organization);
        CollectionAdministration readOnly = new(fixture.Factory,
            new RestrictedAccess(organization, Permissions.CollectionRead), fixture.Clock);
        CollectionAdminView view = await readOnly.ReadAsync(new(Guid.CreateVersion7(), true), CancellationToken.None);
        Assert.AreEqual(1, view.PendingJobs, "Pending KPI must not depend on the 100-row history window.");
        Assert.IsFalse(view.Jobs.Any(item => item.State == "Pending"));
        Assert.AreEqual(0, view.AttentionJobs, "A later successful run resolves an older failed-run attention signal.");
        Assert.IsNotNull(view.Searches.Single().LastRun);
        Assert.AreEqual(3, view.Searches.Single().LastRun!.NewListingsCount);
        Assert.AreEqual(2, view.Searches.Single().LastRun!.ChangedListingsCount);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => readOnly.EnqueueAsync(new(Guid.CreateVersion7(), true),
            search.Id, "read-only", CancellationToken.None));
    }

    [TestMethod]
    public async Task ArchivedSearchCanBeEditedButMustMoveBeforeReenabling()
    {
        await using var f = await SchedulingFixture.CreateAsync("archive-edit@test.invalid", new(2026, 9, 17, 5, 0, 0, TimeSpan.Zero));
        Guid groupId = await f.Admin.CreateGroupAsync(f.Owner, "Архив", 0, "group", default);
        await f.Admin.CreateSearchAsync(f.Owner, new("Ссылка", CatalogSource.Avito,
            "https://www.avito.ru/moskva/zemelnye_uchastki", 10, groupId), "create", default);
        var search = (await f.Admin.ReadAsync(f.Owner, default)).Searches.Single();
        await f.Admin.UpdateSearchAsync(f.Owner, new(search.Id, search.Revision, search.Label, search.Source,
            search.Url, 10, groupId, new(CollectionScheduleKind.Manual), false), "pause", default);
        var view = await f.Admin.ReadAsync(f.Owner, default);
        await f.Admin.ArchiveGroupAsync(f.Owner, groupId, view.Groups.Single().Revision, "archive", default);
        search = view.Searches.Single();
        var edit = new UpdateSearch(search.Id, search.Revision, "Исправленное имя", search.Source, search.Url,
            10, groupId, new(CollectionScheduleKind.Manual), false);
        await f.Admin.UpdateSearchAsync(f.Owner, edit, "edit", default);
        var saved = (await f.Admin.ReadAsync(f.Owner, default)).Searches.Single();
        Assert.AreEqual("Исправленное имя", saved.Label);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => f.Admin.UpdateSearchAsync(f.Owner,
            edit with { ExpectedVersion = saved.Revision, Enabled = true }, "enable-archive", default));
        await f.Admin.UpdateSearchAsync(f.Owner, edit with { ExpectedVersion = saved.Revision, Enabled = true, SearchGroupId = null }, "move", default);
        Assert.IsTrue((await f.Admin.ReadAsync(f.Owner, default)).Searches.Single().Enabled);
    }

    private sealed class SchedulingFixture : IAsyncDisposable
    {
        private readonly PostgresSandbox sandbox;
        private readonly ServiceProvider services;
        public IDbContextFactory<LandErpDbContext> Factory { get; }
        public CollectionAdministration Admin { get; }
        public Subject Owner { get; }
        public Clock Clock { get; }

        private SchedulingFixture(PostgresSandbox sandbox, ServiceProvider services, Guid ownerId, Clock clock)
        {
            this.sandbox = sandbox;
            this.services = services;
            Factory = services.GetRequiredService<IDbContextFactory<LandErpDbContext>>();
            Admin = new(Factory, services.GetRequiredService<IAccessControl>(), clock);
            Owner = new(ownerId, true);
            Clock = clock;
        }

        public static async Task<SchedulingFixture> CreateAsync(string login, DateTimeOffset now)
        {
            PostgresSandbox sandbox = await PostgresSandbox.CreateAsync();
            await using (LandErpDbContext migrator = sandbox.Context()) await migrator.Database.MigrateAsync();
            await using ServiceProvider bootstrap = IdentityOrganizationTests.Services(sandbox.MigratorConnection);
            Guid ownerId = await IdentityOrganizationTests.BootstrapAsync(bootstrap, login, "Schedule behavior");
            await IdentityOrganizationTests.EnableMfaAsync(bootstrap, ownerId);
            await sandbox.GrantRuntimeAsync();
            ServiceProvider services = IdentityOrganizationTests.Services(sandbox.RuntimeConnection);
            return new(sandbox, services, ownerId, new Clock(now));
        }

        public async ValueTask DisposeAsync()
        {
            await services.DisposeAsync();
            await sandbox.DisposeAsync();
        }
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan value) => now += value;
    }

    private sealed class RestrictedAccess(AccessContext context, params string[] allowed) : IAccessControl
    {
        public Task<AccessContext> ResolveAsync(Subject subject, CancellationToken cancellationToken) => Task.FromResult(context);
        public Task<AccessContext> RequireAsync(Subject subject, string permission, CancellationToken cancellationToken) =>
            allowed.Contains(permission, StringComparer.Ordinal)
                ? Task.FromResult(context)
                : Task.FromException<AccessContext>(new AccessDeniedException());
    }

    [TestMethod]
    public async Task ImmediateFirstRunKeepsTwoDailySlotsAndInvalidCreateLeavesNoRows()
    {
        DateTimeOffset start = new(2026, 9, 17, 14, 0, 0, TimeSpan.Zero); // 17:00 Moscow
        await using var f = await SchedulingFixture.CreateAsync("first-run@test.invalid", start);
        var schedule = new CollectionSchedule(CollectionScheduleKind.FixedTimes, FixedTimes: ["09:00", "16:00"]);
        await f.Admin.CreateSearchAsync(f.Owner, new("Первый сбор", CatalogSource.Avito,
            "https://www.avito.ru/moskva/zemelnye_uchastki", 10, Schedule: schedule, RunImmediately: true), "create", default);
        var view = await f.Admin.ReadAsync(f.Owner, default);
        Assert.AreEqual("Europe/Moscow", view.BusinessTimeZone);
        Assert.AreEqual(1, view.PendingJobs);
        Assert.AreEqual(new DateTimeOffset(2026, 9, 18, 6, 0, 0, TimeSpan.Zero), view.Searches.Single().NextRunAt);
        Assert.IsNull(view.Jobs.Single().ScheduledFor);
        Assert.IsNull(view.Jobs.Single().Agent);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => f.Admin.CreateSearchAsync(f.Owner,
            new("Некорректный", CatalogSource.Avito, "https://www.avito.ru/moskva/zemelnye_uchastki", 10,
                Schedule: new(CollectionScheduleKind.Interval), RunImmediately: true), "invalid", default));
        await using var db = await f.Factory.CreateDbContextAsync();
        Assert.AreEqual(1, await db.SearchConfigurations.CountAsync());
        Assert.AreEqual(1, await db.CollectionJobs.CountAsync());
    }

    [TestMethod]
    public async Task MetadataEditPreservesOverdueBoundaryAndPreviewDoesNotWrite()
    {
        DateTimeOffset start = new(2026, 9, 17, 5, 0, 0, TimeSpan.Zero);
        await using var f = await SchedulingFixture.CreateAsync("preserve-run@test.invalid", start);
        var schedule = new CollectionSchedule(CollectionScheduleKind.Interval, 60);
        await f.Admin.CreateSearchAsync(f.Owner, new("Исходный", CatalogSource.Avito,
            "https://www.avito.ru/moskva/zemelnye_uchastki", 10, Schedule: schedule), "create", default);
        var search = (await f.Admin.ReadAsync(f.Owner, default)).Searches.Single();
        f.Clock.Advance(TimeSpan.FromHours(2));
        var preview = await f.Admin.PreviewScheduleAsync(f.Owner, schedule, search.Id, true, default);
        Assert.AreEqual(start.AddHours(1), preview, "An overdue run must not be silently postponed.");
        await f.Admin.UpdateSearchAsync(f.Owner, new(search.Id, search.Revision, "Новое название", search.Source,
            search.Url, 20, null, schedule, true), "rename", default);
        var updated = (await f.Admin.ReadAsync(f.Owner, default)).Searches.Single();
        Assert.AreEqual(preview, updated.NextRunAt);
        var changedPreview = await f.Admin.PreviewScheduleAsync(f.Owner, new(CollectionScheduleKind.Interval, 120), search.Id, true, default);
        Assert.AreEqual(start.AddHours(4), changedPreview);
        Assert.AreEqual(preview, (await f.Admin.ReadAsync(f.Owner, default)).Searches.Single().NextRunAt);
        await f.Admin.UpdateSearchAsync(f.Owner, new(updated.Id, updated.Revision, updated.Label, updated.Source,
            updated.Url, updated.MaxPages, null, schedule, false), "pause", default);
        var paused = (await f.Admin.ReadAsync(f.Owner, default)).Searches.Single();
        Assert.IsNull(paused.NextRunAt);
        await f.Admin.UpdateSearchAsync(f.Owner, new(paused.Id, paused.Revision, paused.Label, paused.Source,
            paused.Url, paused.MaxPages, null, schedule, true), "resume", default);
        Assert.AreEqual(start.AddHours(3), (await f.Admin.ReadAsync(f.Owner, default)).Searches.Single().NextRunAt);
    }

    [TestMethod]
    public async Task DailyNineAndSixteenSkipDuplicateWhilePendingAndRecoverOneMissedSlot()
    {
        await using var f = await SchedulingFixture.CreateAsync("two-slots@test.invalid", new(2026, 9, 17, 5, 0, 0, TimeSpan.Zero));
        await f.Admin.CreateSearchAsync(f.Owner, new("Два раза", CatalogSource.Cian, "https://www.cian.ru/cat.php", 10,
            Schedule: new(CollectionScheduleKind.FixedTimes, FixedTimes: ["16:00", "09:00"])), "create", default);
        var scheduler = new CollectionScheduler(f.Factory, f.Clock);
        f.Clock.Advance(TimeSpan.FromHours(1));
        Assert.AreEqual(1, await scheduler.RunDueAsync(default));
        Assert.AreEqual(new DateTimeOffset(2026, 9, 17, 13, 0, 0, TimeSpan.Zero),
            (await f.Admin.ReadAsync(f.Owner, default)).Searches.Single().NextRunAt);
        f.Clock.Advance(TimeSpan.FromHours(7));
        Assert.AreEqual(0, await scheduler.RunDueAsync(default), "Pending work prevents a second active job.");
        await using (var db = await f.Factory.CreateDbContextAsync())
        {
            (await db.CollectionJobs.SingleAsync()).State = CollectionJobState.Completed;
            await db.SaveChangesAsync();
        }
        f.Clock.Advance(TimeSpan.FromDays(3));
        Assert.AreEqual(1, await scheduler.RunDueAsync(default), "Only one catch-up job after downtime.");
        Assert.AreEqual(0, await scheduler.RunDueAsync(default));
    }
}
