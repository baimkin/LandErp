using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Infrastructure.Modules.Catalog;
using LandErp.Infrastructure.Modules.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace LandErp.Foundation.Tests;

[TestClass]
[TestCategory("PostgreSQL")]
public sealed class CatalogBulkArchiveTests
{
    private static IncomingCatalogReadFilter Filter(string text = "", Guid? zone = null,
        CatalogDisposition? state = CatalogDisposition.Incoming, int size = 40) =>
        new(new(Text: text, Disposition: state, Size: size), SearchGroupId: zone,
            WorkingScope: new(IncomingCatalogMode.AllListings));

    private static async Task<Guid> ManualAsync(ProcurementTests.Phase1Fixture f,
        string title, string location = "Чувашия, Новочебоксарск", decimal price = 1000m,
        Subject? subject = null) => await f.Workspace.CreateManualAsync(subject ?? f.Manager,
            new(CatalogSource.Manual, title, location, price, 100m, null, null, null, null, "Тест массового архива"),
            "bulk-archive-setup", CancellationToken.None);

    private static async Task<Guid> ZoneAsync(ProcurementTests.Phase1Fixture f,
        LandErp.Infrastructure.Modules.Collection.CollectionAdministration administration)
    {
        await f.SetExplicitAccessAsync(f.ManagerEmployeeId,
            ProcurementTestsHelper.ProcurementManagerAccess(AccessScope.Department) with { CanManageSearchGroups = true });
        Guid zone = await administration.CreateGroupAsync(f.Manager, "Зона массового архива", 1,
            "bulk-zone", CancellationToken.None);
        await using var db = f.Sandbox.Context();
        foreach (var search in await db.SearchConfigurations.Where(item => item.OrganizationId == f.OrganizationId).ToArrayAsync())
            search.SearchGroupId = zone;
        await db.SaveChangesAsync();
        return zone;
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task SelectedArchiveAndOptionalMedianExclusionCommitWithHistory(bool exclude)
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        var pair = await f.IngestMarketplacePairAsync();
        Guid zone = await ZoneAsync(f, pair.Administration);
        Guid untouched = await ManualAsync(f, "Не выбрано");
        await using (var db = f.Sandbox.Context())
        {
            foreach (var item in await db.Listings.Where(item => item.OrganizationId == f.OrganizationId).ToArrayAsync())
            {
                item.IncludeInCalculation = true;
                if (item.Id == pair.CianId)
                {
                    item.Disposition = CatalogDisposition.Monitoring;
                    item.TargetTotalPrice = 100m;
                    item.TargetPricePerSotka = 10m;
                    item.MonitoringStartedAt = DateTimeOffset.UtcNow;
                }
            }
            await db.SaveChangesAsync();
        }
        await using var source = NpgsqlDataSource.Create(f.Sandbox.RuntimeConnection);
        var access = new EmployeeAccessService(f.Factory);
        var presets = new IncomingFilterPresetService(source, access);
        var service = new CatalogArchiveService(f.Factory, access, presets, TimeProvider.System);
        var reads = new IncomingCatalogReadService(f.Factory, access, f.Workspace, TimeProvider.System, presets);
        var left = (await f.Workspace.ReadItemAsync(f.Manager, pair.AvitoId, CancellationToken.None)).Item;
        var right = (await f.Workspace.ReadItemAsync(f.Manager, pair.CianId, CancellationToken.None)).Item;
        var selection = new CatalogBulkSelection(Filter(zone: zone, state: null, size: 1),
            [new(left.Id, left.Version), new(right.Id, right.Version)]);
        var preview = await service.PreviewAsync(f.Manager, selection, exclude, CancellationToken.None);
        Assert.AreEqual(2, preview.Total);
        Assert.AreEqual(2, preview.WouldArchive);
        Assert.AreEqual(exclude ? 2 : 0, preview.WouldExcludeFromMedian);
        Assert.AreEqual(CatalogDisposition.Incoming,
            (await f.Workspace.ReadItemAsync(f.Manager, left.Id, CancellationToken.None)).Item.Disposition);
        var result = await service.ApplyAsync(f.Manager, selection, exclude, "Локация: ошибочный регион",
            preview.Stamp, "bulk-archive", CancellationToken.None);
        Assert.AreEqual(2, result.Archived);
        Assert.AreEqual(exclude ? 2 : 0, result.ExcludedFromMedian);
        await using (var db = f.Sandbox.Context())
        {
            var archived = await db.Listings.Where(item => item.Id == left.Id || item.Id == right.Id).ToArrayAsync();
            Assert.IsTrue(archived.All(item => item.Disposition == CatalogDisposition.Dismissed
                && item.IncludeInCalculation == !exclude && !item.AttentionRequired
                && item.TargetTotalPrice == null && item.TargetPricePerSotka == null && item.MonitoringStartedAt == null));
            Assert.IsTrue(archived.All(item => item.Version > (item.Id == left.Id ? left.Version : right.Version)));
            Assert.AreEqual(2, await db.CatalogEvents.CountAsync(item => (item.CatalogItemId == left.Id || item.CatalogItemId == right.Id)
                && item.Kind == CatalogEventKind.Classified && item.Message == "Не подходит: Локация: ошибочный регион"));
            Assert.AreEqual(exclude ? 2 : 0, await db.CatalogEvents.CountAsync(item =>
                (item.CatalogItemId == left.Id || item.CatalogItemId == right.Id)
                && item.Kind == CatalogEventKind.CalculationParticipationChanged));
            Assert.AreEqual(2, await db.AuditEvents.CountAsync(item => item.Action == "CatalogDispositionChanged"
                && item.ActorId == f.Manager.UserId && item.CorrelationId == "bulk-archive"));
            Assert.AreEqual(exclude ? 2 : 0, await db.AuditEvents.CountAsync(item => item.Action == "CatalogCalculationParticipationChanged"
                && item.ActorId == f.Manager.UserId && item.CorrelationId == "bulk-archive"));
            Assert.AreEqual(0, await db.PropertyCases.CountAsync());
            Assert.IsTrue((await db.Listings.SingleAsync(item => item.Id == untouched)).IncludeInCalculation);
            Assert.AreEqual(CatalogDisposition.Incoming, (await db.Listings.SingleAsync(item => item.Id == untouched)).Disposition);
        }
        Assert.AreEqual(0, (await reads.ReadAsync(f.Manager, Filter(zone: zone), CancellationToken.None)).Total);
        Assert.AreEqual(2, (await reads.ReadAsync(f.Manager, Filter(zone: zone) with
            { WorkingScope = new(IncomingCatalogMode.Archive) }, CancellationToken.None)).Total);
        // Exercise the existing server ingestion boundary only: no browser, parser or live source.
        await f.IngestChangedAvitoAsync(pair.Agent, pair.Administration, 1_900_000m);
        var afterCollection = (await f.Workspace.ReadItemAsync(f.Manager, left.Id, CancellationToken.None)).Item;
        Assert.AreEqual(CatalogDisposition.Dismissed, afterCollection.Disposition);
        Assert.AreEqual(!exclude, afterCollection.IncludeInCalculation);
    }

    [TestMethod]
    public async Task AllSavedFilterResultsAcrossPagesRespectTextAndDeselectedRows()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        Guid[] ids = new Guid[5];
        for (int i = 0; i < ids.Length; i++) ids[i] = await ManualAsync(f, "Участок " + i, price: 1000m + i);
        Guid moscow = await ManualAsync(f, "Другой регион", "Московская область", 1000m);
        Guid costly = await ManualAsync(f, "Дорогое в Чувашии", price: 2000m);
        await using var source = NpgsqlDataSource.Create(f.Sandbox.RuntimeConnection);
        var access = new EmployeeAccessService(f.Factory);
        var presets = new IncomingFilterPresetService(source, access);
        var service = new CatalogArchiveService(f.Factory, access, presets, TimeProvider.System);
        var reads = new IncomingCatalogReadService(f.Factory, access, f.Workspace, TimeProvider.System, presets);
        var criteria = new IncomingFilterPresetCriteriaV1(2, null, null, CatalogDisposition.Incoming,
            CatalogAgeRange.Any, null, 1100m, null, null, null, null, [], false,
            IncomingCatalogSortField.Price, IncomingCatalogSortDirection.Ascending);
        var saved = await presets.CreateAsync(f.Manager, new(null, "Недорогие", criteria), CancellationToken.None);
        var filter = Filter("Чувашия", size: 2) with { WorkingScope = new(SelectedPresetId: saved.Id) };
        var first = await reads.ReadAsync(f.Manager, filter, CancellationToken.None);
        Assert.AreEqual(5, first.Total);
        Assert.AreEqual(2, first.Items.Count);
        var selection = new CatalogBulkSelection(filter, ExcludedIds: [ids[0], ids[4]]);
        var preview = await service.PreviewAsync(f.Manager, selection, false, CancellationToken.None);
        Assert.AreEqual(3, preview.Total);
        var lastPage = await service.PreviewAsync(f.Manager, selection with
            { Filter = filter with { Base = filter.Base with { Offset = 4 } } }, false, CancellationToken.None);
        Assert.AreEqual(preview.Stamp, lastPage.Stamp);
        Assert.AreEqual(3, (await service.ApplyAsync(f.Manager, selection, false, "Локация",
            preview.Stamp, "bulk-all", CancellationToken.None)).Archived);
        await using var db = f.Sandbox.Context();
        CollectionAssert.AreEquivalent(ids[1..4], await db.Listings.Where(item => item.Disposition == CatalogDisposition.Dismissed)
            .Select(item => item.Id).ToArrayAsync());
        Assert.AreEqual(4, await db.Listings.CountAsync(item => item.Disposition == CatalogDisposition.Incoming));
        Assert.AreEqual(CatalogDisposition.Incoming, (await db.Listings.SingleAsync(item => item.Id == moscow)).Disposition);
        Assert.AreEqual(CatalogDisposition.Incoming, (await db.Listings.SingleAsync(item => item.Id == costly)).Disposition);
    }

    [TestMethod]
    public async Task ChangedVersionOrCohortOrCheckboxRejectsWholeBatchWithoutHistory()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        Guid left = await ManualAsync(f, "Конфликт 1");
        Guid right = await ManualAsync(f, "Конфликт 2");
        await using var source = NpgsqlDataSource.Create(f.Sandbox.RuntimeConnection);
        var access = new EmployeeAccessService(f.Factory);
        var presets = new IncomingFilterPresetService(source, access);
        var service = new CatalogArchiveService(f.Factory, access, presets, TimeProvider.System);
        var selection = new CatalogBulkSelection(Filter("Конфликт"));
        var preview = await service.PreviewAsync(f.Manager, selection, false, CancellationToken.None);
        await using (var db = f.Sandbox.Context())
        {
            var item = await db.Listings.SingleAsync(item => item.Id == right);
            item.Price = 1100m;
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(() => service.ApplyAsync(f.Manager,
            selection, false, "Локация", preview.Stamp, "bulk-stale", CancellationToken.None));
        preview = await service.PreviewAsync(f.Manager, selection, false, CancellationToken.None);
        Guid added = await ManualAsync(f, "Конфликт 3");
        await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(() => service.ApplyAsync(f.Manager,
            selection, false, "Локация", preview.Stamp, "bulk-new", CancellationToken.None));
        var current = (await f.Workspace.ReadItemAsync(f.Manager, left, CancellationToken.None)).Item;
        await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(() => service.PreviewAsync(f.Manager,
            new(Filter(), [new(left, current.Version - 1)]), false, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.PreviewAsync(f.Manager,
            new(Filter(), [new(left, current.Version), new(left, current.Version)]), false, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.PreviewAsync(f.Manager,
            new(Filter(), [new(left, current.Version)], [right]), false, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.ApplyAsync(f.Manager,
            selection, false, " ", preview.Stamp, "bulk-reason", CancellationToken.None));
        await using var check = f.Sandbox.Context();
        Assert.AreEqual(3, await check.Listings.CountAsync(item => item.Disposition == CatalogDisposition.Incoming));
        Assert.AreEqual(0, await check.CatalogEvents.CountAsync(item => item.Kind == CatalogEventKind.Classified));
        Assert.AreEqual(0, await check.AuditEvents.CountAsync(item => item.Action == "CatalogDispositionChanged"));
        Assert.AreNotEqual(Guid.Empty, added);
    }

    [TestMethod]
    public async Task ProcessingAndZoneRightsAndTenantIsolationAreRecheckedOnApply()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        var pair = await f.IngestMarketplacePairAsync();
        Guid zone = await ZoneAsync(f, pair.Administration);
        Guid foreign = await ManualAsync(f, "Чужое объявление", subject: f.ForeignOwner);
        await using var source = NpgsqlDataSource.Create(f.Sandbox.RuntimeConnection);
        var access = new EmployeeAccessService(f.Factory);
        var presets = new IncomingFilterPresetService(source, access);
        var service = new CatalogArchiveService(f.Factory, access, presets, TimeProvider.System);
        var filter = Filter(zone: zone);
        var selection = new CatalogBulkSelection(filter);
        var preview = await service.PreviewAsync(f.Manager, selection, true, CancellationToken.None);
        // A stamp for archiving alone cannot authorize turning on median exclusion later.
        var archiveOnly = await service.PreviewAsync(f.Manager, selection, false, CancellationToken.None);
        await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(() => service.ApplyAsync(f.Manager,
            selection, true, "Локация", archiveOnly.Stamp, "bulk-checkbox", CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.PreviewAsync(f.Manager,
            new(Filter()), true, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(() => service.PreviewAsync(f.Manager,
            new(Filter(), [new(foreign, 1)]), false, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(() => service.PreviewAsync(f.ForeignOwner,
            new(Filter(), [new(pair.AvitoId, 1)]), false, CancellationToken.None));
        var foreignPreview = await service.PreviewAsync(f.ForeignOwner, new(Filter()), false, CancellationToken.None);
        Assert.AreEqual(1, foreignPreview.Total);
        await using (var db = f.Sandbox.Context())
        {
            (await db.SearchGroups.SingleAsync(item => item.Id == zone)).OwnerEmployeeId = null;
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => service.ApplyAsync(f.Manager,
            selection, true, "Локация", preview.Stamp, "bulk-zone-revoked", CancellationToken.None));
        await f.SetExplicitAccessAsync(f.ManagerEmployeeId,
            EmployeeAccessRules.NoAccess with { IncomingAccess = IncomingAccessLevel.Read });
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => service.PreviewAsync(f.Manager,
            selection, false, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => service.ApplyAsync(f.Manager,
            selection, false, "Локация", archiveOnly.Stamp, "bulk-process-revoked", CancellationToken.None));
        await using var check = f.Sandbox.Context();
        Assert.AreEqual(0, await check.CatalogEvents.CountAsync(item => item.Kind == CatalogEventKind.Classified));
        Assert.AreEqual(3, await check.Listings.CountAsync());
    }

    [TestMethod]
    public async Task DatabaseFailureRollsBackArchiveMedianAndHistoryTogether()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        var pair = await f.IngestMarketplacePairAsync();
        Guid zone = await ZoneAsync(f, pair.Administration);
        await using (var db = f.Sandbox.Context())
        {
            foreach (var item in await db.Listings.ToArrayAsync()) item.IncludeInCalculation = true;
            await db.SaveChangesAsync();
            // Fail a real database insert after the command has staged both kinds of changes.
            // This disposable sandbox is the only database receiving this test trigger.
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION catalog.reject_bulk_archive_test() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW.message LIKE 'Не подходит:%' THEN
                        RAISE EXCEPTION 'Synthetic archive failure';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER reject_bulk_archive_test BEFORE INSERT ON catalog.events
                    FOR EACH ROW EXECUTE FUNCTION catalog.reject_bulk_archive_test();
                """);
        }
        await using var source = NpgsqlDataSource.Create(f.Sandbox.RuntimeConnection);
        var access = new EmployeeAccessService(f.Factory);
        var presets = new IncomingFilterPresetService(source, access);
        var service = new CatalogArchiveService(f.Factory, access, presets, TimeProvider.System);
        var selection = new CatalogBulkSelection(Filter(zone: zone));
        var preview = await service.PreviewAsync(f.Manager, selection, true, CancellationToken.None);
        var failure = await Assert.ThrowsExactlyAsync<DbUpdateException>(() => service.ApplyAsync(f.Manager, selection,
            true, "Локация", preview.Stamp, "bulk-rollback", CancellationToken.None));
        Assert.AreEqual("P0001", ((PostgresException)failure.InnerException!).SqlState);
        await using var check = f.Sandbox.Context();
        Assert.AreEqual(2, await check.Listings.CountAsync(item => item.Disposition == CatalogDisposition.Incoming && item.IncludeInCalculation));
        Assert.AreEqual(0, await check.CatalogEvents.CountAsync(item => item.Kind == CatalogEventKind.Classified
            || item.Kind == CatalogEventKind.CalculationParticipationChanged));
        Assert.AreEqual(0, await check.AuditEvents.CountAsync(item => item.CorrelationId == "bulk-rollback"));
    }

    [TestMethod]
    public async Task LinkedProcurementCaseAndSpecificArchivedClassificationsArePreserved()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        Guid id = await ManualAsync(f, "Связанный источник");
        var taken = await f.Workspace.TakeToWorkAsync(f.Manager, new(id), "bulk-take", CancellationToken.None);
        var before = await f.ReadCaseAsync(taken.CaseId);
        Guid sold = await ManualAsync(f, "Проданный источник");
        var soldItem = (await f.Workspace.ReadItemAsync(f.Manager, sold, CancellationToken.None)).Item;
        await f.Workspace.SetDispositionAsync(f.Manager, new(sold, soldItem.Version, CatalogDisposition.Sold,
            "Подтверждено продавцом"), "bulk-sold", CancellationToken.None);
        await using var source = NpgsqlDataSource.Create(f.Sandbox.RuntimeConnection);
        var access = new EmployeeAccessService(f.Factory);
        var presets = new IncomingFilterPresetService(source, access);
        var service = new CatalogArchiveService(f.Factory, access, presets, TimeProvider.System);
        var selection = new CatalogBulkSelection(Filter(state: null));
        var preview = await service.PreviewAsync(f.Manager, selection, false, CancellationToken.None);
        Assert.AreEqual(1, preview.WouldArchive);
        Assert.AreEqual(1, preview.AlreadyArchived);
        var result = await service.ApplyAsync(f.Manager, selection, false, "Локация", preview.Stamp,
            "bulk-linked", CancellationToken.None);
        Assert.AreEqual(1, result.Archived);
        var after = await f.ReadCaseAsync(taken.CaseId);
        Assert.AreEqual(before.StageId, after.StageId);
        Assert.AreEqual(before.Version, after.Version);
        await using var db = f.Sandbox.Context();
        Assert.AreEqual(1, await db.PropertyCaseSourceLinks.CountAsync(item => item.PropertyCaseId == taken.CaseId && item.Confirmed));
        Assert.AreEqual(CatalogDisposition.Dismissed, (await db.Listings.SingleAsync(item => item.Id == id)).Disposition);
        Assert.AreEqual(CatalogDisposition.Sold, (await db.Listings.SingleAsync(item => item.Id == sold)).Disposition);
        Assert.AreEqual(1, await db.AuditEvents.CountAsync(item => item.Action == "CatalogDispositionChanged" && item.CorrelationId == "bulk-linked"));
    }
}
