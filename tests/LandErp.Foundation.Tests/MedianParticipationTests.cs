using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.Collection.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Collector.Contracts.V1;
using LandErp.Infrastructure.Modules.Catalog;
using LandErp.Infrastructure.Modules.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using LandErp.Infrastructure.Modules.IdentityAccess;
using LandErp.Infrastructure.Persistence;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace LandErp.Foundation.Tests;

[TestClass]
[TestCategory("PostgreSQL")]
public sealed class MedianParticipationTests
{
    private sealed record ZoneContext(Guid Id, Guid JobId, Guid AgentId);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, ZoneContext> Zones = new();

    private static IncomingFilterPresetCriteriaV1 Criteria(bool automatic = false, Guid? group = null) => new(
        2, null, null, null, CatalogAgeRange.Any, null, null, null, null, null, null, [], false,
        IncomingCatalogSortField.PricePerSotka, IncomingCatalogSortDirection.Ascending,
        SearchGroupId: group, AutoIncludeNewInCalculation: automatic);

    private static async Task<Guid> Manual(ProcurementTests.Phase1Fixture f, string title = "Аналог M01",
        decimal? price = 1000m, decimal? area = 100m)
    {
        Guid id = await f.Workspace.CreateManualAsync(f.Manager,
            new(CatalogSource.Manual, title, "Химки", price, area, null, null, null, null, "M01 ручной ввод"),
            "m01", CancellationToken.None);
        if (Zones.TryGetValue(f.OrganizationId, out ZoneContext? zone)) await AttachAsync(f, zone, id);
        return id;
    }

    private static async Task<ZoneContext> ZoneAsync(ProcurementTests.Phase1Fixture f)
    {
        await f.SetExplicitAccessAsync(f.ManagerEmployeeId,
            ProcurementTestsHelper.ProcurementManagerAccess(AccessScope.AssignedObjects) with
            { CanManageSearchGroups = true });
        CollectionAdministration administration = new(f.Factory, TimeProvider.System);
        Guid groupId = await administration.CreateGroupAsync(f.Manager, "Зона менеджера M01", 1,
            "m01-zone", CancellationToken.None);
        ZoneContext zone = new(groupId, Guid.CreateVersion7(), Guid.CreateVersion7());
        await using LandErpDbContext db = f.Sandbox.Context();
        Guid searchId = Guid.CreateVersion7();
        db.CollectorAgents.Add(new()
        {
            Id = zone.AgentId, OrganizationId = f.OrganizationId, Name = "M01 fixture Parser",
            CredentialHash = "fixture"
        });
        db.SearchConfigurations.Add(new()
        {
            Id = searchId, OrganizationId = f.OrganizationId, SearchGroupId = groupId,
            Label = "M01 fixture search", Source = CatalogSource.Avito,
            Url = "https://www.avito.ru/moskva/zemelnye_uchastki", Enabled = true
        });
        db.CollectionJobs.Add(new()
        {
            Id = zone.JobId, OrganizationId = f.OrganizationId, AgentId = zone.AgentId,
            SearchId = searchId, State = CollectionJobState.Completed,
            CreatedAt = DateTimeOffset.UtcNow, CompletedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        Zones[f.OrganizationId] = zone;
        return zone;
    }

    private static async Task AttachAsync(ProcurementTests.Phase1Fixture f, ZoneContext zone, Guid listingId)
    {
        await using LandErpDbContext db = f.Sandbox.Context();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid id = Guid.CreateVersion7();
        db.ListingObservations.Add(new()
        {
            Id = id, ListingId = listingId, AgentId = zone.AgentId, JobId = zone.JobId,
            ObservationKey = "m01-zone-" + id.ToString("N"), ContentHash = id.ToString("N"),
            PayloadJson = "{}", ChangesJson = "[]", ObservedAt = now, RecordedAt = now
        });
        await db.SaveChangesAsync();
    }

    [TestMethod]
    public async Task AllResultsMinusExclusionsAcrossPagesUsesSamePreviewAndApplyCohort()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        ZoneContext zone = await ZoneAsync(f);
        await using var source = NpgsqlDataSource.Create(f.Sandbox.RuntimeConnection);
        var access = new EmployeeAccessService(f.Factory);
        var presets = new IncomingFilterPresetService(source, access);
        var service = new CatalogCalculationService(f.Factory, access, presets, TimeProvider.System);
        var reads = new IncomingCatalogReadService(f.Factory, access, f.Workspace, TimeProvider.System, presets);
        for (int i = 0; i < 5; i++) await Manual(f, "UX аналог " + i, 1000m + i);
        var request = new IncomingCatalogReadFilter(new("UX аналог", Size: 2), SortField: IncomingCatalogSortField.Price,
            SearchGroupId: zone.Id, WorkingScope: new(IncomingCatalogMode.AllListings));
        var first = await reads.ReadAsync(f.Manager, request, CancellationToken.None);
        var second = await reads.ReadAsync(f.Manager, request with { Base = request.Base with { Offset = 2 } }, CancellationToken.None);
        Guid[] excluded = [first.Items[0].Id, second.Items[0].Id];
        var selection = new CatalogCalculationSelection(request, ExcludedIds: excluded);
        var preview = await service.PreviewAsync(f.Manager, selection, true, CancellationToken.None);
        Assert.AreEqual(3, preview.Total);
        var otherPage = await service.PreviewAsync(f.Manager, selection with { Filter = request with { Base = request.Base with { Offset = 4 } } }, true, CancellationToken.None);
        Assert.AreEqual(preview.Stamp, otherPage.Stamp);
        await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(() => service.ApplyAsync(f.Manager,
            selection with { ExcludedIds = [first.Items[1].Id, second.Items[1].Id] }, true, preview.Stamp, "wrong-cohort", CancellationToken.None));
        Assert.AreEqual(3, (await service.ApplyAsync(f.Manager, selection, true, preview.Stamp, "ux", CancellationToken.None)).Changed);
        await using var db = f.Sandbox.Context();
        Assert.AreEqual(0, await db.Listings.CountAsync(x => excluded.Contains(x.Id) && x.IncludeInCalculation));
        Assert.AreEqual(3, await db.Listings.CountAsync(x => x.IncludeInCalculation));
        Assert.AreEqual(3,(await reads.ReadAsync(f.Manager,request with {IncludedInMedian=true},CancellationToken.None)).Total);
        Assert.AreEqual(2,(await reads.ReadAsync(f.Manager,request with {IncludedInMedian=false},CancellationToken.None)).Total);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.PreviewAsync(f.Manager,
            new(request, [new(first.Items[0].Id, first.Items[0].Version, zone.Id)], excluded), true, CancellationToken.None));
        var stale = await service.PreviewAsync(f.Manager, selection, false, CancellationToken.None);
        await Manual(f, "UX аналог новый", 1009m);
        await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(() => service.ApplyAsync(f.Manager, selection, false, stale.Stamp, "changed", CancellationToken.None));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => service.PreviewAsync(new(Guid.NewGuid(), false), selection, true, CancellationToken.None));
    }

    [TestMethod]
    public async Task SavedChoiceSurvivesInvalidDataAndRecoveryButManualExclusionStaysOff()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        ZoneContext zone = await ZoneAsync(f);
        await using var source = NpgsqlDataSource.Create(f.Sandbox.RuntimeConnection);
        var access = new EmployeeAccessService(f.Factory);
        IncomingFilterPresetService presets = new(source, access);
        CatalogCalculationService service = new(f.Factory, access, presets, TimeProvider.System);
        IncomingCatalogReadService reads = new(f.Factory, access, f.Workspace, TimeProvider.System, presets);
        var pair = await f.IngestMarketplacePairAsync();
        Guid id = pair.AvitoId;
        await AttachAsync(f, zone, id);
        var initial = (await f.Workspace.ReadItemAsync(f.Manager, id, CancellationToken.None)).Item;
        await service.SetAsync(f.Manager, new(id, initial.Version, zone.Id), true, "m01-retain", CancellationToken.None);

        // Exercise the real server observation boundary: zero is delivered as known data, not Missing.
        await f.IngestChangedAsync(ListingSource.Avito, pair.Agent, pair.Administration, 0m, "m01-price-zero");
        var invalid = (await f.Workspace.ReadItemAsync(f.Manager, id, CancellationToken.None)).Item;
        Assert.IsTrue(invalid.IncludeInCalculation);
        Assert.AreEqual("Нужна положительная цена", invalid.CalculationIneligibility);
        Assert.AreEqual(1, (await service.SetAsync(f.Manager, new(id, invalid.Version, zone.Id), true, "m01-noop", CancellationToken.None)).AlreadySet);
        var request = new IncomingCatalogReadFilter(new(), SearchGroupId: zone.Id,
            WorkingScope: new(IncomingCatalogMode.AllListings));
        var row = (await reads.ReadAsync(f.Manager, request, CancellationToken.None)).Items.Single(item => item.Id == id);
        Assert.IsTrue(row.IncludeInCalculation);
        Assert.AreEqual(invalid.CalculationIneligibility, row.CalculationIneligibility);
        var preview = await service.PreviewAsync(f.Manager, new(request, [new(id, row.Version, zone.Id)]), true, CancellationToken.None);
        Assert.AreEqual(1, preview.AlreadySet);
        Assert.AreEqual(0, preview.WouldChange);
        Assert.AreEqual(0, preview.Ineligible);

        await f.IngestChangedAsync(ListingSource.Avito, pair.Agent, pair.Administration, 2_000_000m, "m01-price-restored");
        var restored = (await f.Workspace.ReadItemAsync(f.Manager, id, CancellationToken.None)).Item;
        Assert.IsTrue(restored.IncludeInCalculation);
        Assert.IsNull(restored.CalculationIneligibility);

        // Missing observations preserve known facts; direct fixture changes represent actual loss
        // of persisted facts or an incompatible currency, which ingress itself rejects.
        foreach (var facts in new (decimal? Price, decimal? Area, string Currency)[]
            { (null, 1500m, "RUB"), (2_000_000m, null, "RUB"), (2_000_000m, 0m, "RUB"), (2_000_000m, 1500m, "USD") })
        {
            await using (var db = f.Sandbox.Context())
            {
                var item = await db.Listings.SingleAsync(item => item.Id == id);
                item.Price = facts.Price; item.AreaSquareMeters = facts.Area; item.Currency = facts.Currency;
                await db.SaveChangesAsync();
            }
            var detail = (await reads.ReadDetailAsync(f.Manager, id, CancellationToken.None)).Detail.Item;
            Assert.IsTrue(detail.IncludeInCalculation);
            Assert.IsNotNull(detail.CalculationIneligibility);
        }
        invalid = (await f.Workspace.ReadItemAsync(f.Manager, id, CancellationToken.None)).Item;
        Assert.AreEqual(1, (await service.SetAsync(f.Manager, new(id, invalid.Version, zone.Id), false, "m01-manual-off", CancellationToken.None)).Changed);
        await using (var db = f.Sandbox.Context())
        {
            var item = await db.Listings.SingleAsync(item => item.Id == id);
            item.Currency = "RUB"; item.AreaSquareMeters = 1500m;
            await db.SaveChangesAsync();
        }
        await f.IngestChangedAsync(ListingSource.Avito, pair.Agent, pair.Administration, 1_900_000m, "m01-off-stays-off");
        restored = (await f.Workspace.ReadItemAsync(f.Manager, id, CancellationToken.None)).Item;
        Assert.IsFalse(restored.IncludeInCalculation);
        Assert.IsNull(restored.CalculationIneligibility);
        await using (var db = f.Sandbox.Context())
            Assert.AreEqual(2, await db.CatalogEvents.CountAsync(item => item.CatalogItemId == id
                && item.Kind == CatalogEventKind.CalculationParticipationChanged), "Only the two explicit choices change participation history.");
    }

    [TestMethod]
    public async Task MigrationStartsExistingFalseAndSingleCommandsEnforceVersionsRightsAndEligibility()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        ZoneContext zone = await ZoneAsync(f);
        Guid id = await Manual(f);
        await using (var db = f.Sandbox.Context())
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20260921225000_AccessV1Cutover");
            string sql = migrator.GenerateScript("20260921225000_AccessV1Cutover", "20260926120000_CatalogCalculationParticipation");
            StringAssert.Contains(sql, "DEFAULT FALSE");
            StringAssert.Contains(sql, "COMMENT ON COLUMN catalog.listings.include_in_calculation");
            await migrator.MigrateAsync();
            Assert.IsFalse(db.Database.HasPendingModelChanges());
            Assert.IsFalse((await db.Listings.SingleAsync(item => item.Id == id)).IncludeInCalculation);
        }
        await f.SetExplicitAccessAsync(f.ManagerEmployeeId,
            ProcurementTestsHelper.ProcurementManagerAccess(AccessScope.AssignedObjects) with
            { CanManageSearchGroups = true });
        await using (var db = f.Sandbox.Context())
        {
            (await db.SearchGroups.SingleAsync(item => item.Id == zone.Id)).OwnerEmployeeId = f.ManagerEmployeeId;
            await db.SaveChangesAsync();
        }
        await using var source = NpgsqlDataSource.Create(f.Sandbox.RuntimeConnection);
        var access = new EmployeeAccessService(f.Factory);
        IncomingFilterPresetService presets = new(source, access);
        CatalogCalculationService service = new(f.Factory, access, presets, TimeProvider.System);
        var initial = (await f.Workspace.ReadItemAsync(f.Manager, id, CancellationToken.None)).Item;
        Assert.AreEqual(1, (await service.SetAsync(f.Manager, new(id, initial.Version, zone.Id), true, "m01", CancellationToken.None)).Changed);
        await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(() => service.SetAsync(f.Manager,
            new(id, initial.Version, zone.Id), false, "m01-stale", CancellationToken.None));
        var current = (await f.Workspace.ReadItemAsync(f.Manager, id, CancellationToken.None)).Item;
        Assert.IsTrue(current.IncludeInCalculation);
        Assert.AreEqual(1, (await service.SetAsync(f.Manager, new(id, current.Version, zone.Id), true, "m01-repeat", CancellationToken.None)).AlreadySet);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => service.SetAsync(f.ForeignOwner,
            new(id, current.Version, zone.Id), false, "foreign", CancellationToken.None));
        await using (var db = f.Sandbox.Context())
        {
            Assert.AreEqual(1, await db.CatalogEvents.CountAsync(item => item.CatalogItemId == id
                && item.Kind == CatalogEventKind.CalculationParticipationChanged));
            Assert.AreEqual(1, await db.AuditEvents.CountAsync(item => item.ObjectId == id
                && item.Action == "CatalogCalculationParticipationChanged" && item.ActorId == f.Manager.UserId));
        }
        Guid invalid = await Manual(f, price: null);
        var invalidItem = (await f.Workspace.ReadItemAsync(f.Manager, invalid, CancellationToken.None)).Item;
        var rejected = await service.SetAsync(f.Manager, new(invalid, invalidItem.Version, zone.Id), true, "m01", CancellationToken.None);
        Assert.AreEqual(1, rejected.Ineligible);
        Assert.IsNotNull(invalidItem.CalculationIneligibility);
        foreach (var values in new[] { (Price: 0m, Area: 100m, Currency: "RUB"), (Price: 100m, Area: -1m, Currency: "RUB"), (Price: 100m, Area: 100m, Currency: "USD") })
        {
            Guid badId = await Manual(f, price: values.Price, area: values.Area);
            if (values.Currency != "RUB")
            {
                await using var db = f.Sandbox.Context();
                (await db.Listings.SingleAsync(item => item.Id == badId)).Currency = values.Currency;
                await db.SaveChangesAsync();
            }
            var bad = (await f.Workspace.ReadItemAsync(f.Manager, badId, CancellationToken.None)).Item;
            Assert.AreEqual(1, (await service.SetAsync(f.Manager, new(badId, bad.Version, zone.Id), true, "m01", CancellationToken.None)).Ineligible);
        }
        await using (var connection = new NpgsqlConnection(f.Sandbox.RuntimeConnection))
        {
            await connection.OpenAsync();
            await using var metadata = new NpgsqlCommand("SELECT col_description('catalog.listings'::regclass, attnum) FROM pg_attribute WHERE attrelid='catalog.listings'::regclass AND attname='include_in_calculation'", connection);
            StringAssert.Contains((string)(await metadata.ExecuteScalarAsync())!, "Общая для организации");
        }
        CatalogCalculationSelection all = new(new(new(), SearchGroupId: zone.Id,
            WorkingScope: new(IncomingCatalogMode.AllListings)));
        var authorizedPreview = await service.PreviewAsync(f.Manager, all, false, CancellationToken.None);
        await f.SetExplicitAccessAsync(f.ManagerEmployeeId, EmployeeAccessRules.NoAccess with { IncomingAccess = IncomingAccessLevel.Read });
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => service.SetAsync(f.Manager,
            new(id, current.Version, zone.Id), false, "m01", CancellationToken.None));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => service.PreviewAsync(f.Manager, all, true, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => service.ApplyAsync(f.Manager, all, false, authorizedPreview.Stamp, "m01", CancellationToken.None));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => presets.CreateAsync(f.Manager,
            new(null, "Нет права", Criteria(true)), CancellationToken.None));
    }

    [TestMethod]
    public async Task MedianChangesRequireOwnedZoneAndListingProvenanceWhileOwnerCanUseAnyZone()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        ZoneContext owned = await ZoneAsync(f);
        Guid listingId = await Manual(f, "Проверка владения зоной");
        CollectionAdministration administration = new(f.Factory, TimeProvider.System);
        Guid unrelated = await administration.CreateGroupAsync(f.Owner, "Другая зона", 2,
            "m01-unrelated", CancellationToken.None);
        await using var source = NpgsqlDataSource.Create(f.Sandbox.RuntimeConnection);
        var access = new EmployeeAccessService(f.Factory);
        IncomingFilterPresetService presets = new(source, access);
        CatalogCalculationService service = new(f.Factory, access, presets, TimeProvider.System);
        var item = (await f.Workspace.ReadItemAsync(f.Manager, listingId, CancellationToken.None)).Item;

        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => service.SetAsync(f.Manager,
            new(listingId, item.Version, unrelated), true, "m01-wrong-owner", CancellationToken.None));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => service.SetAsync(f.Owner,
            new(listingId, item.Version, unrelated), true, "m01-wrong-provenance", CancellationToken.None));
        Assert.AreEqual(1, (await service.SetAsync(f.Owner,
            new(listingId, item.Version, owned.Id), true, "m01-elevated", CancellationToken.None)).Changed);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.PreviewAsync(f.Owner,
            new(new(new(), WorkingScope: new(IncomingCatalogMode.AllListings))), false, CancellationToken.None));
    }

    [TestMethod]
    public async Task BulkUsesWholeSharedSelectionPreviewConflictsArchiveAndHonestCounts()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        ZoneContext zone = await ZoneAsync(f);
        await using var source = NpgsqlDataSource.Create(f.Sandbox.RuntimeConnection);
        var access = new EmployeeAccessService(f.Factory);
        IncomingFilterPresetService presets = new(source, access);
        CatalogCalculationService service = new(f.Factory, access, presets, TimeProvider.System);
        IncomingCatalogReadService reads = new(f.Factory, access, f.Workspace, TimeProvider.System, presets);
        for (int i = 0; i < 5; i++) await Manual(f, "Аналог " + i, 1000m + i);
        await Manual(f, "Аналог без площади", area: null);
        await Manual(f, "Вне отбора", price: 9000m);
        var first = await presets.CreateAsync(f.Manager, new(zone.Id, "Первый", Criteria(group: zone.Id) with { MaxTotalPrice = 1002m }), CancellationToken.None);
        await presets.CreateAsync(f.Manager, new(zone.Id, "Пересечение", Criteria(group: zone.Id) with { MinTotalPrice = 1001m, MaxTotalPrice = 1004m }), CancellationToken.None);
        var request = new IncomingCatalogReadFilter(new("Аналог", Size: 2), SearchGroupId: zone.Id,
            SortField: IncomingCatalogSortField.PricePerSotka, SortDirection: IncomingCatalogSortDirection.Ascending, WorkingScope: new());
        var page = await reads.ReadAsync(f.Manager, request, CancellationToken.None);
        Assert.AreEqual(6, page.Total);
        Assert.AreEqual(2, page.Items.Count);
        CatalogCalculationSelection all = new(request);
        var preview = await service.PreviewAsync(f.Manager, all, true, CancellationToken.None);
        Assert.AreEqual(page.Total, preview.Total);
        Assert.AreEqual(5, preview.WouldChange);
        Assert.AreEqual(1, preview.Ineligible);
        var result = await service.ApplyAsync(f.Manager, all, true, preview.Stamp, "m01", CancellationToken.None);
        Assert.AreEqual(5, result.Changed);
        await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(() => service.ApplyAsync(f.Manager, all,
            true, preview.Stamp, "m01-repeat", CancellationToken.None));
        var again = await service.PreviewAsync(f.Manager, all, true, CancellationToken.None);
        Assert.AreEqual(5, again.AlreadySet);
        page = await reads.ReadAsync(f.Manager, request, CancellationToken.None);
        CatalogCalculationSelection selected = new(request, page.Items.Select(item => new CatalogCalculationTarget(item.Id, item.Version, zone.Id)).ToArray());
        var selectedPreview = await service.PreviewAsync(f.Manager, selected, false, CancellationToken.None);
        Assert.AreEqual(2, selectedPreview.Total);
        Assert.AreEqual(2, (await service.ApplyAsync(f.Manager, selected, false, selectedPreview.Stamp, "m01", CancellationToken.None)).Changed);
        var stale = await service.PreviewAsync(f.Manager, all, false, CancellationToken.None);
        var changedPreset = await presets.UpdateAsync(f.Manager, new(first.Id, first.Version, first.Name,
            first.Criteria with { MaxTotalPrice = 999m }), CancellationToken.None);
        await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(() => service.ApplyAsync(f.Manager, all,
            false, stale.Stamp, "m01", CancellationToken.None));
        Assert.AreEqual(3, await f.CountAsync(db => db.Listings.CountAsync(item => item.IncludeInCalculation)));
        await presets.DeleteAsync(f.Manager, new(changedPreset.Id, changedPreset.Version), CancellationToken.None);
        Assert.AreEqual(3, await f.CountAsync(db => db.Listings.CountAsync(item => item.IncludeInCalculation)));
        var remaining = (await reads.ReadAsync(f.Manager, request, CancellationToken.None)).Items[0];
        await f.Workspace.SetDispositionAsync(f.Manager, new(remaining.Id, remaining.Version, CatalogDisposition.Sold, "Продано M01"), "m01", CancellationToken.None);
        var archive = new IncomingCatalogReadFilter(new("Аналог", MaxPrice: 1m), SearchGroupId: zone.Id,
            WorkingScope: new(IncomingCatalogMode.Archive));
        var archivePage = await reads.ReadAsync(f.Manager, archive, CancellationToken.None);
        var archivePreview = await service.PreviewAsync(f.Manager, new(archive), true, CancellationToken.None);
        Assert.AreEqual(1, archivePage.Total);
        Assert.AreEqual(archivePage.Total, archivePreview.Total);
        Assert.AreEqual(0, archivePreview.Ineligible);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => service.PreviewAsync(new(Guid.NewGuid(), false), all, true, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => service.PreviewAsync(f.ForeignOwner, selected, false, CancellationToken.None));
    }

    [TestMethod]
    public async Task SavingAutomaticFilterIncludesExistingMatchesAndNewIngressPreservesManualExclusion()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        ZoneContext zone = await ZoneAsync(f);
        await using var source = NpgsqlDataSource.Create(f.Sandbox.RuntimeConnection);
        var access = new EmployeeAccessService(f.Factory);
        IncomingFilterPresetService presets = new(source, access);
        CatalogCalculationService service = new(f.Factory, access, presets, TimeProvider.System);
        var pair = await f.IngestMarketplacePairAsync();
        Guid group = zone.Id;
        Guid searchId;
        await using (var db = await f.Factory.CreateDbContextAsync())
        {
            Guid fixtureSearchId = await db.CollectionJobs.Where(item => item.Id == zone.JobId)
                .Select(item => item.SearchId).SingleAsync();
            var search = await db.SearchConfigurations.SingleAsync(item =>
                item.Source == CatalogSource.Avito && item.Id != fixtureSearchId);
            search.SearchGroupId = group;
            searchId = search.Id;
            await db.SaveChangesAsync();
        }
        var saved = await presets.CreateAsync(f.Manager, new(group, "Авто M01", Criteria(group: group)), CancellationToken.None);
        Assert.IsFalse(saved.Criteria.AutoIncludeNewInCalculation);
        saved = await presets.UpdateAsync(f.Manager, new(saved.Id, saved.Version, saved.Name, saved.Criteria with { AutoIncludeNewInCalculation = true }), CancellationToken.None);
        Assert.AreEqual(1, await f.CountAsync(db => db.Listings.CountAsync(item => item.IncludeInCalculation)));
        await f.IngestChangedAvitoAsync(pair.Agent, pair.Administration, 1_900_000m);
        Assert.AreEqual(1, await f.CountAsync(db => db.Listings.CountAsync(item => item.IncludeInCalculation)));
        Guid manual = await Manual(f);
        Assert.IsFalse((await f.Workspace.ReadItemAsync(f.Manager, manual, CancellationToken.None)).Item.IncludeInCalculation);
        CollectorGateway gateway = new(f.Factory, TimeProvider.System);
        await pair.Administration.EnqueueAsync(f.Owner, searchId, "m01", CancellationToken.None);
        var work = (await gateway.ClaimAsync(pair.Agent, CancellationToken.None))!;
        ListingData data = new()
        {
            Source = ListingSource.Avito, ExternalId = "30001", Url = "https://www.avito.ru/moskva/zemelnye_uchastki/30001",
            ObservedAt = DateTimeOffset.UtcNow, AdapterVersion = "m01", Provenance = "M01 test",
            Title = new(FieldPresence.Present, "Новый аналог"), Price = new(FieldPresence.Present, "1000 ₽", 1000m),
            AreaSquareMeters = new(FieldPresence.Present, "100 м²", 100m), Location = new(FieldPresence.Present, "Химки")
        };
        CollectionResult delivery = new(Guid.CreateVersion7(), work.JobId, work.LeaseId, CollectionOutcome.Success, [new("m01-new", data)], true);
        await gateway.AcceptAsync(pair.Agent, delivery, CancellationToken.None);
        Guid id;
        await using (var db = await f.Factory.CreateDbContextAsync())
        {
            var created = await db.Listings.SingleAsync(item => item.ExternalId == "30001");
            id = created.Id;
            var currentPresets = await presets.ReadAsync(f.Manager, CancellationToken.None);
            IncomingCatalogReadService reads = new(f.Factory, access, f.Workspace, TimeProvider.System, presets);
            var matches = await reads.ReadAsync(f.Manager, new(new(), WorkingScope: new(SelectedPresetId: saved.Id)), CancellationToken.None);
            Guid? actualGroup = await (from observation in db.ListingObservations
                join job in db.CollectionJobs on observation.JobId equals job.Id
                join search in db.SearchConfigurations on job.SearchId equals search.Id
                where observation.ListingId == created.Id select search.SearchGroupId).SingleAsync();
            Assert.IsTrue(created.IncludeInCalculation, $"Auto={currentPresets.Single(p => p.Id == saved.Id).Criteria.AutoIncludeNewInCalculation}; Issue={currentPresets.Single(p => p.Id == saved.Id).CompatibilityIssue}; Eligible={CatalogCalculationEligibility.Reason(created)}; InSelection={matches.Items.Any(p => p.Id == created.Id)}; GroupMatches={actualGroup == group}");
            await service.SetAsync(f.Manager, new(id, created.Version, group), false, "m01", CancellationToken.None);
        }
        await gateway.AcceptAsync(pair.Agent, delivery, CancellationToken.None);
        await pair.Administration.EnqueueAsync(f.Owner, searchId, "m01", CancellationToken.None);
        work = (await gateway.ClaimAsync(pair.Agent, CancellationToken.None))!;
        await gateway.AcceptAsync(pair.Agent, new(Guid.CreateVersion7(), work.JobId, work.LeaseId, CollectionOutcome.Success,
            [new("m01-repeat-observation", data with { ObservedAt = data.ObservedAt.AddMinutes(2) })], true), CancellationToken.None);
        Assert.IsFalse((await f.Workspace.ReadItemAsync(f.Manager, id, CancellationToken.None)).Item.IncludeInCalculation);
        var global = await presets.CreateAsync(f.Manager, new(null, "Ручные новые", Criteria(true)), CancellationToken.None);
        Guid autoManual = await Manual(f);
        Assert.IsTrue((await f.Workspace.ReadItemAsync(f.Manager, autoManual, CancellationToken.None)).Item.IncludeInCalculation);
        await presets.UpdateAsync(f.Manager, new(global.Id, global.Version, global.Name, global.Criteria with { AutoIncludeNewInCalculation = false }), CancellationToken.None);
        Assert.IsTrue((await f.Workspace.ReadItemAsync(f.Manager, autoManual, CancellationToken.None)).Item.IncludeInCalculation);
        Assert.IsTrue((await f.Workspace.ReadItemAsync(f.Manager, manual, CancellationToken.None)).Item.IncludeInCalculation);
        // A legacy JSON without the optional flag stays false; a broken legacy binding never executes.
        await using (var connection = new NpgsqlConnection(f.Sandbox.MigratorConnection))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("UPDATE catalog.incoming_filter_presets SET criteria_json = (criteria_json - 'autoIncludeNewInCalculation') WHERE id = @id", connection);
            command.Parameters.AddWithValue("id", global.Id);
            await command.ExecuteNonQueryAsync();
            command.CommandText = "UPDATE catalog.incoming_filter_presets SET criteria_json = jsonb_set(jsonb_set(criteria_json, '{schemaVersion}', '1'), '{searchConfigurationId}', to_jsonb(@missing::text)) WHERE id = @id";
            command.Parameters["id"].Value = saved.Id;
            command.Parameters.AddWithValue("missing", Guid.NewGuid().ToString());
            await command.ExecuteNonQueryAsync();
        }
        var loaded = await presets.ReadAsync(f.Manager, CancellationToken.None);
        Assert.IsFalse(loaded.Single(item => item.Id == global.Id).Criteria.AutoIncludeNewInCalculation);
        Assert.IsNotNull(loaded.Single(item => item.Id == saved.Id).CompatibilityIssue);
        Guid skipped = await Manual(f);
        Assert.IsFalse((await f.Workspace.ReadItemAsync(f.Manager, skipped, CancellationToken.None)).Item.IncludeInCalculation);
    }

    [TestMethod]
    public async Task ExcludingStatesClearParticipationSoldDismissedRemainAndUnlinkCanOptIn()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        ZoneContext zone = await ZoneAsync(f);
        await using var source = NpgsqlDataSource.Create(f.Sandbox.RuntimeConnection);
        var access = new EmployeeAccessService(f.Factory);
        IncomingFilterPresetService presets = new(source, access);
        CatalogCalculationService service = new(f.Factory, access, presets, TimeProvider.System);
        foreach (var state in new[] { CatalogDisposition.Fake, CatalogDisposition.RemovedAtSource, CatalogDisposition.Sold, CatalogDisposition.Dismissed })
        {
            Guid id = await Manual(f);
            var item = (await f.Workspace.ReadItemAsync(f.Manager, id, CancellationToken.None)).Item;
            await service.SetAsync(f.Manager, new(id, item.Version, zone.Id), true, "m01", CancellationToken.None);
            item = (await f.Workspace.ReadItemAsync(f.Manager, id, CancellationToken.None)).Item;
            await f.Workspace.SetDispositionAsync(f.Manager, new(id, item.Version, state, "M01 состояние"), "m01", CancellationToken.None);
            item = (await f.Workspace.ReadItemAsync(f.Manager, id, CancellationToken.None)).Item;
            bool eligible = state is CatalogDisposition.Sold or CatalogDisposition.Dismissed;
            Assert.AreEqual(eligible, item.IncludeInCalculation);
            if (!eligible) Assert.AreEqual(1, (await service.SetAsync(f.Manager, new(id, item.Version, zone.Id), true, "m01", CancellationToken.None)).Ineligible);
            else
            {
                await service.SetAsync(f.Manager, new(id, item.Version, zone.Id), false, "m01", CancellationToken.None);
                item = (await f.Workspace.ReadItemAsync(f.Manager, id, CancellationToken.None)).Item;
                Assert.AreEqual(1, (await service.SetAsync(f.Manager, new(id, item.Version, zone.Id), true, "m01", CancellationToken.None)).Changed);
            }
        }
        Guid left = await Manual(f, "Один объект слева");
        Guid right = await Manual(f, "Один объект справа");
        var listing = (await f.Workspace.ReadItemAsync(f.Manager, left, CancellationToken.None)).Item;
        await service.SetAsync(f.Manager, new(left, listing.Version, zone.Id), true, "m01", CancellationToken.None);
        listing = (await f.Workspace.ReadItemAsync(f.Manager, left, CancellationToken.None)).Item;
        await f.Workspace.LinkCatalogItemsAsSameObjectAsync(f.Manager, new(left, listing.Version, right, "M01 дубль"), "m01", CancellationToken.None);
        listing = (await f.Workspace.ReadItemAsync(f.Manager, left, CancellationToken.None)).Item;
        Assert.AreEqual(CatalogDisposition.Duplicate, listing.Disposition);
        Assert.IsFalse(listing.IncludeInCalculation);
        await f.Workspace.UnlinkCatalogItemFromObjectGroupAsync(f.Manager, new(left, listing.Version, "M01 возврат"), "m01", CancellationToken.None);
        listing = (await f.Workspace.ReadItemAsync(f.Manager, left, CancellationToken.None)).Item;
        Assert.IsFalse(listing.IncludeInCalculation);
        await f.Workspace.LinkCatalogItemsAsSameObjectAsync(f.Manager, new(left, listing.Version, right, "M01 дубль снова"), "m01", CancellationToken.None);
        listing = (await f.Workspace.ReadItemAsync(f.Manager, left, CancellationToken.None)).Item;
        await f.Workspace.UnlinkCatalogItemFromObjectGroupAsync(f.Manager, new(left, listing.Version, "M01 возврат с отметкой", true), "m01", CancellationToken.None);
        Assert.IsTrue((await f.Workspace.ReadItemAsync(f.Manager, left, CancellationToken.None)).Item.IncludeInCalculation);
    }
}
