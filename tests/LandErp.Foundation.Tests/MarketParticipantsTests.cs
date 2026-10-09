using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Infrastructure.Modules.Catalog;
using LandErp.Infrastructure.Modules.IdentityAccess;
using LandErp.Infrastructure.Modules.Overview;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace LandErp.Foundation.Tests;

[TestClass]
[TestCategory("PostgreSQL")]
public sealed class MarketParticipantsTests
{
    private static IncomingCatalogReadFilter Request(Guid group, string text = "") => new(
        new(text, Disposition: CatalogDisposition.Incoming, MaxPrice: 1m, Size: 2),
        Preset: IncomingCatalogPreset.Incomplete, SearchGroupId: group,
        WorkingScope: new(IncomingCatalogMode.Participants, Slice: IncomingCatalogPreset.New));

    private static Guid Add(LandErpDbContext db, Guid organization, decimal? price, decimal? area,
        CatalogDisposition state = CatalogDisposition.Incoming, bool marked = true, string currency = "RUB",
        Guid? objectGroup = null)
    {
        Guid id = Guid.CreateVersion7();
        db.Listings.Add(new()
        {
            Id = id, OrganizationId = organization, Source = CatalogSource.Avito,
            Title = "Источник " + id, Price = price, AreaSquareMeters = area, Currency = currency,
            Disposition = state, IncludeInCalculation = marked, ObjectGroupId = objectGroup,
            ReceivedAt = DateTimeOffset.UtcNow.AddDays(-500), RecordedAt = DateTimeOffset.UtcNow,
            ChangedAt = DateTimeOffset.UtcNow
        });
        return id;
    }

    private static void Observe(LandErpDbContext db, Guid listing, Guid agent, Guid job, DateTimeOffset at)
    {
        string key = Guid.NewGuid().ToString();
        db.ListingObservations.Add(new()
        {
            Id = Guid.CreateVersion7(), ListingId = listing, AgentId = agent, JobId = job,
            ObservationKey = key, ContentHash = key, PayloadJson = "{}", ChangesJson = "[]",
            ObservedAt = at, RecordedAt = DateTimeOffset.UtcNow
        });
    }

    [TestMethod]
    public async Task ExactPricesMembershipOldSettingsRecoveryAndAccessStayConsistent()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        var pair = await f.IngestMarketplacePairAsync();
        var admin = pair.Administration;
        Guid a = await admin.CreateGroupAsync(f.Owner, "A рынок", 1, "m02", CancellationToken.None);
        Guid b = await admin.CreateGroupAsync(f.Owner, "B рынок", 2, "m02", CancellationToken.None);
        Guid empty = await admin.CreateGroupAsync(f.Owner, "C пусто", 3, "m02", CancellationToken.None);
        Guid first, second, sold, invalid;
        await using (var db = f.Sandbox.Context())
        {
            var avito = await db.SearchConfigurations.SingleAsync(item => item.Source == CatalogSource.Avito);
            var cian = await db.SearchConfigurations.SingleAsync(item => item.Source == CatalogSource.Cian);
            avito.SearchGroupId = a; cian.SearchGroupId = b;
            Guid jobA = await db.CollectionJobs.Where(item => item.SearchId == avito.Id).Select(item => item.Id).SingleAsync();
            Guid jobB = await db.CollectionJobs.Where(item => item.SearchId == cian.Id).Select(item => item.Id).SingleAsync();
            first = Add(db, f.OrganizationId, 100m, 100m);
            second = Add(db, f.OrganizationId, 400m, 200m, CatalogDisposition.Dismissed);
            sold = Add(db, f.OrganizationId, 2700m, 300m, CatalogDisposition.Sold);
            invalid = Add(db, f.OrganizationId, null, 100m);
            Guid[] excluded = [Add(db, f.OrganizationId, 500m, 100m, marked: false),
                Add(db, f.OrganizationId, 500m, 0m), Add(db, f.OrganizationId, 500m, 100m, currency: "USD"),
                Add(db, f.OrganizationId, 500m, 100m, CatalogDisposition.Duplicate),
                Add(db, f.OrganizationId, 500m, 100m, CatalogDisposition.Fake),
                Add(db, f.OrganizationId, 500m, 100m, CatalogDisposition.RemovedAtSource)];
            Add(db, f.OrganizationId, 9999m, 100m); // Marked but no group facts.
            DateTimeOffset old = DateTimeOffset.UtcNow.AddDays(-400);
            foreach (Guid id in new[] { first, second, sold, invalid }.Concat(excluded)) Observe(db, id, pair.Agent.AgentId, jobA, old);
            Observe(db, first, pair.Agent.AgentId, jobA, old.AddDays(1));
            Observe(db, first, pair.Agent.AgentId, jobB, old);
            Observe(db, second, pair.Agent.AgentId, jobB, old);
            await db.SaveChangesAsync();
        }
        OverviewService overview = new(f.Factory, TimeProvider.System);
        var legacyVersion = (await overview.ReadMarketGroupsAsync(f.Owner, new(), CancellationToken.None)).Items.Single(item => item.SearchGroupId == a).Settings.Version;
        await overview.SaveMarketSettingsAsync(f.Owner, new(a, legacyVersion, 7, [IncomingLandType.Snt], 5000m, 6000m), "m02-old", CancellationToken.None);
        var market = await overview.ReadMarketGroupsAsync(f.Owner, new(), CancellationToken.None);
        var groupA = market.Items.Single(item => item.SearchGroupId == a);
        Assert.AreEqual(3, groupA.IncludedCount);
        Assert.AreEqual(400m, groupA.AveragePricePerSotka);
        Assert.AreEqual(200m, groupA.MedianPricePerSotka);
        Assert.AreEqual(150m, market.Items.Single(item => item.SearchGroupId == b).MedianPricePerSotka);
        Assert.AreEqual(150m, market.Items.Single(item => item.SearchGroupId == b).AveragePricePerSotka);
        Assert.IsNull(market.Items.Single(item => item.SearchGroupId == empty).MedianPricePerSotka);
        Assert.IsNull(market.Items.Single(item => item.SearchGroupId == empty).AveragePricePerSotka);
        Assert.AreEqual(0, market.Items.Single(item => item.SearchGroupId == empty).IncludedCount);
        Assert.IsTrue(groupA.CanReadParticipants);
        await using var source = NpgsqlDataSource.Create(f.Sandbox.RuntimeConnection);
        var access = new EmployeeAccessService(f.Factory);
        IncomingFilterPresetService presets = new(source, access);
        IncomingCatalogReadService reads = new(f.Factory, access, f.Workspace, TimeProvider.System, presets);
        CatalogCalculationService commands = new(f.Factory, access, presets, TimeProvider.System);
        var page = await reads.ReadAsync(f.Manager, Request(a), CancellationToken.None);
        Assert.AreEqual(groupA.IncludedCount, page.Total);
        Assert.AreEqual(page.Total, page.ParticipantTotal);
        var next = await reads.ReadAsync(f.Manager, Request(a) with { Base = Request(a).Base with { Offset = 2 } }, CancellationToken.None);
        CollectionAssert.AreEquivalent(new[] { first, second, sold }, page.Items.Concat(next.Items).Select(item => item.Id).ToArray());
        var search = await reads.ReadAsync(f.Manager, Request(a, sold.ToString()), CancellationToken.None);
        Assert.AreEqual(1, search.Total);
        Assert.AreEqual(3, search.ParticipantTotal);
        var preview = await commands.PreviewAsync(f.Owner, new(Request(b)), false, CancellationToken.None);
        Assert.AreEqual(2, preview.Total);
        await commands.ApplyAsync(f.Owner, new(Request(b)), false, preview.Stamp, "m02-exclude", CancellationToken.None);
        market = await overview.ReadMarketGroupsAsync(f.Owner, new(), CancellationToken.None);
        Assert.AreEqual(0, market.Items.Single(item => item.SearchGroupId == b).IncludedCount);
        Assert.AreEqual(1, market.Items.Single(item => item.SearchGroupId == a).IncludedCount);
        Assert.AreEqual(900m, market.Items.Single(item => item.SearchGroupId == a).MedianPricePerSotka);
        Assert.AreEqual(900m, market.Items.Single(item => item.SearchGroupId == a).AveragePricePerSotka);
        await using (var db = f.Sandbox.Context())
        {
            (await db.Listings.SingleAsync(item => item.Id == invalid)).Price = 300m;
            await db.SaveChangesAsync();
        }
        market = await overview.ReadMarketGroupsAsync(f.Owner, new(), CancellationToken.None);
        Assert.AreEqual(600m, market.Items.Single(item => item.SearchGroupId == a).MedianPricePerSotka);
        Assert.AreEqual(600m, market.Items.Single(item => item.SearchGroupId == a).AveragePricePerSotka);
        Assert.AreEqual(2, (await reads.ReadAsync(f.Manager, Request(a), CancellationToken.None)).Total);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => reads.ReadAsync(f.ForeignOwner, Request(a), CancellationToken.None));
        Assert.AreEqual(0, (await overview.ReadMarketGroupsAsync(f.ForeignOwner, new(), CancellationToken.None)).Total);
        await f.SetExplicitAccessAsync(f.ManagerEmployeeId, EmployeeAccessRules.NoAccess with { CollectionAccess = CollectionAccessLevel.Read });
        Assert.IsFalse((await overview.ReadMarketGroupsAsync(f.Manager, new(), CancellationToken.None)).Items[0].CanReadParticipants);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => reads.ReadAsync(f.Manager, Request(a), CancellationToken.None));
        await f.SetExplicitAccessAsync(f.ManagerEmployeeId, EmployeeAccessRules.NoAccess with { IncomingAccess = IncomingAccessLevel.Read });
        Assert.AreEqual(2, (await reads.ReadAsync(f.Manager, Request(a), CancellationToken.None)).Total);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => overview.ReadMarketGroupsAsync(f.Manager, new(), CancellationToken.None));
    }

    [TestMethod]
    public async Task RepresentativeUsesLatestObservationInEachGroupAndExclusionRevealsNextCandidate()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        var pair = await f.IngestMarketplacePairAsync();
        Guid a = await pair.Administration.CreateGroupAsync(f.Owner, "A объект", 1, "m02", CancellationToken.None);
        Guid b = await pair.Administration.CreateGroupAsync(f.Owner, "B объект", 2, "m02", CancellationToken.None);
        Guid older, fresher, tie;
        await using (var db = f.Sandbox.Context())
        {
            var searches = await db.SearchConfigurations.OrderBy(item => item.Source).ToArrayAsync();
            searches[0].SearchGroupId = a; searches[1].SearchGroupId = b;
            Guid searchA = searches[0].Id, searchB = searches[1].Id;
            Guid jobA = await db.CollectionJobs.Where(item => item.SearchId == searchA).Select(item => item.Id).SingleAsync();
            Guid jobB = await db.CollectionJobs.Where(item => item.SearchId == searchB).Select(item => item.Id).SingleAsync();
            // A second search in A proves distinct membership across searches, not only observations.
            Guid extraSearch = Guid.CreateVersion7(), extraJob = Guid.CreateVersion7();
            db.SearchConfigurations.Add(new() { Id = extraSearch, OrganizationId = f.OrganizationId, SearchGroupId = a, Label = "Ещё поиск A", Source = CatalogSource.Avito, Url = "https://www.avito.ru/moskva/zemelnye_uchastki" });
            db.CollectionJobs.Add(new() { Id = extraJob, OrganizationId = f.OrganizationId, SearchId = extraSearch, CreatedAt = DateTimeOffset.UtcNow });
            Guid objectId = Guid.CreateVersion7();
            db.CatalogObjectGroups.Add(new() { Id = objectId, OrganizationId = f.OrganizationId, RecordedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
            older = Add(db, f.OrganizationId, 100m, 100m, CatalogDisposition.Sold, objectGroup: objectId);
            fresher = Add(db, f.OrganizationId, 300m, 100m, CatalogDisposition.Dismissed, objectGroup: objectId);
            tie = Add(db, f.OrganizationId, 500m, 100m, CatalogDisposition.Sold, objectGroup: objectId);
            Guid unmarked = Add(db, f.OrganizationId, 999m, 100m, marked: false, objectGroup: objectId);
            Guid invalid = Add(db, f.OrganizationId, null, 100m, objectGroup: objectId);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            Observe(db, older, pair.Agent.AgentId, jobA, now.AddDays(-5));
            Observe(db, fresher, pair.Agent.AgentId, jobA, now.AddDays(-2));
            Observe(db, tie, pair.Agent.AgentId, jobA, now.AddDays(-2));
            Observe(db, fresher, pair.Agent.AgentId, extraJob, now.AddDays(-3));
            Observe(db, older, pair.Agent.AgentId, jobB, now.AddDays(-1));
            Observe(db, fresher, pair.Agent.AgentId, jobB, now.AddDays(-6));
            Observe(db, unmarked, pair.Agent.AgentId, jobA, now);
            Observe(db, invalid, pair.Agent.AgentId, jobA, now);
            await db.SaveChangesAsync();
        }
        await using var source = NpgsqlDataSource.Create(f.Sandbox.RuntimeConnection);
        var access = new EmployeeAccessService(f.Factory);
        IncomingFilterPresetService presets = new(source, access);
        IncomingCatalogReadService reads = new(f.Factory, access, f.Workspace, TimeProvider.System, presets);
        CatalogCalculationService commands = new(f.Factory, access, presets, TimeProvider.System);
        OverviewService overview = new(f.Factory, TimeProvider.System);
        Guid expected = new[] { fresher, tie }.Order().First();
        var participant = (await reads.ReadAsync(f.Manager, Request(a), CancellationToken.None)).Items.Single();
        Assert.AreEqual(expected, participant.Id);
        Assert.AreEqual(older, (await reads.ReadAsync(f.Manager, Request(b), CancellationToken.None)).Items.Single().Id);
        var market = await overview.ReadMarketGroupsAsync(f.Owner, new(), CancellationToken.None);
        Assert.AreEqual(1, market.Items.Single(item => item.SearchGroupId == a).IncludedCount);
        Assert.AreEqual(participant.PricePerSotka, market.Items.Single(item => item.SearchGroupId == a).MedianPricePerSotka);
        Assert.AreEqual(100m, market.Items.Single(item => item.SearchGroupId == b).AveragePricePerSotka);
        await commands.SetAsync(f.Owner, new(participant.Id, participant.Version, a), false, "m02", CancellationToken.None);
        Guid replacement = expected == fresher ? tie : fresher;
        Assert.AreEqual(replacement, (await reads.ReadAsync(f.Manager, Request(a), CancellationToken.None)).Items.Single().Id);
        market = await overview.ReadMarketGroupsAsync(f.Owner, new(), CancellationToken.None);
        Assert.AreEqual(1, market.Items.Single(item => item.SearchGroupId == a).IncludedCount);
        Assert.AreEqual(expected == fresher ? 500m : 300m, market.Items.Single(item => item.SearchGroupId == a).AveragePricePerSotka);
    }
}
