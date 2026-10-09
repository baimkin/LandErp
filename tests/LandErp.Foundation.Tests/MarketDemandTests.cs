using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Overview.Contracts;
using LandErp.Infrastructure.Modules.Catalog;
using LandErp.Infrastructure.Modules.Collection;
using LandErp.Infrastructure.Modules.IdentityAccess;
using LandErp.Infrastructure.Modules.Overview;
using LandErp.Infrastructure.Modules.Procurement;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace LandErp.Foundation.Tests;

[TestClass]
[TestCategory("PostgreSQL")]
public sealed class MarketDemandTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static IncomingFilterPresetCriteriaV1 Criteria(Guid? group = null) => new(
        2, null, null, null, CatalogAgeRange.Any, null, 1m, null, null, null, null, [], false,
        IncomingCatalogSortField.PricePerSotka, IncomingCatalogSortDirection.Ascending, SearchGroupId: group);

    [TestMethod]
    public void LocationEconomicsCalculatesPerSotkaAndTargetWithoutPersistedDerivedValues()
    {
        Assert.AreEqual(250_000m, LocationEconomics.PricePerSotka(2_000_000m, 800m));
        Assert.AreEqual(800_000m, LocationEconomics.TargetPropertyPrice(100_000m, 800m));
        Assert.AreEqual(33.33m, LocationEconomics.PricePerSotka(100m, 300m));
        Assert.AreEqual(300.03m, LocationEconomics.TargetPropertyPrice(100.01m, 300m));
        Assert.IsNull(LocationEconomics.PricePerSotka(2_000_000m, null));
        Assert.IsNull(LocationEconomics.PricePerSotka(2_000_000m, 0m));
        Assert.IsNull(LocationEconomics.TargetPropertyPrice(100_000m, -1m));
        Assert.IsNull(LocationEconomics.TargetPropertyPrice(null, 800m));
    }

    [TestMethod]
    public void LocationEconomicsUiKeepsBothGuidesAllZonesAndExplicitUnavailableCalculations()
    {
        string root = FoundationTests.RepositoryRoot();
        string prices = File.ReadAllText(Path.Combine(root, "src", "LandErp.Server", "Components",
            "Primitives", "GroupMarketPrices.razor"));
        string card = File.ReadAllText(Path.Combine(root, "src", "LandErp.Server", "Components",
            "Procurement", "CaseWorkspace.razor"));
        string drawer = File.ReadAllText(Path.Combine(root, "src", "LandErp.Server", "Components",
            "Pages", "ProcurementQueueV2.razor"));
        string overview = File.ReadAllText(Path.Combine(root, "src", "LandErp.Server", "Components",
            "Pages", "Home.razor"));

        StringAssert.Contains(prices, "Ориентир теста спроса");
        StringAssert.Contains(prices, "Желаемая цена покупки");
        StringAssert.Contains(prices, "Целевая стоимость участка");
        StringAssert.Contains(prices, "Нет площади для расчёта");
        StringAssert.Contains(card, "@foreach (var market in caseMarkets)");
        StringAssert.Contains(card, "PricePerSotkaCaption");
        StringAssert.Contains(drawer, "@foreach (var market in drawerMarkets)");
        StringAssert.Contains(drawer, "Цена объявления");
        StringAssert.Contains(drawer, "Согласовано");
        StringAssert.Contains(overview, "Ориентир теста спроса");
        StringAssert.Contains(overview, "Желаемая цена покупки");
        StringAssert.Contains(overview, "PurchaseTarget=\"true\"");
    }

    [TestMethod]
    public async Task DemandRequiresHeadValidatesOrganizationPriceVersionAndAuditsClear()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        await using var source = NpgsqlDataSource.Create(f.Sandbox.RuntimeConnection);
        var access = new EmployeeAccessService(f.Factory);
        var presets = new IncomingFilterPresetService(source, access);
        var service = new GroupMarketService(f.Factory, access, presets);
        var administration = new CollectionAdministration(f.Factory, TimeProvider.System);
        Guid group = await administration.CreateGroupAsync(f.Owner, "Тест спроса", 0, "m03", Ct);
        var original = (await service.ReadProcurementAsync(f.Head, group, Ct))!;
        Assert.IsNull(original.DemandTestPricePerSotka);
        Assert.IsNull(original.TargetPurchasePricePerSotka);
        Assert.IsTrue(original.CanEditDemand);
        Assert.IsFalse((await service.ReadProcurementAsync(f.Manager, group, Ct))!.CanEditDemand);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => service.SaveDemandAsync(f.Manager, new(group, original.Version, 100m), "m03", Ct));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => service.SaveDemandAsync(f.ForeignOwner, new(group, original.Version, 100m), "m03", Ct));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => service.ReadProcurementAsync(f.ForeignOwner, group, Ct));
        foreach (decimal invalid in new[] { -1m, 0m, .001m, 1000000000000000m })
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.SaveDemandAsync(f.Head, new(group, original.Version, invalid), "m03", Ct));
        await service.SaveDemandAsync(f.Head, new(group, original.Version, 123.455m), "m03-set", Ct);
        var changed = (await service.ReadProcurementAsync(f.Owner, group, Ct))!;
        Assert.AreEqual(123.46m, changed.DemandTestPricePerSotka);
        Assert.IsTrue(changed.Version > original.Version);
        Assert.AreEqual(original.MedianPricePerSotka, changed.MedianPricePerSotka);
        await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(() => service.SaveDemandAsync(f.Head, new(group, original.Version, 500m), "m03-stale", Ct));
        await service.SaveTargetPurchaseAsync(f.Head, new(group, changed.Version, 95_555.555m), "m03-target", Ct);
        var targetChanged = (await service.ReadProcurementAsync(f.Owner, group, Ct))!;
        Assert.AreEqual(95_555.56m, targetChanged.TargetPurchasePricePerSotka);
        Assert.AreEqual(123.46m, targetChanged.DemandTestPricePerSotka);
        await service.SaveDemandAsync(f.Owner, new(group, targetChanged.Version, null), "m03-clear", Ct);
        var cleared = (await service.ReadProcurementAsync(f.Head, group, Ct))!;
        Assert.IsNull(cleared.DemandTestPricePerSotka);
        Assert.AreEqual(95_555.56m, cleared.TargetPurchasePricePerSotka);
        await using (var db = f.Sandbox.Context())
        {
            var audit = await db.AuditEvents.Where(item => item.ObjectId == group && item.Action == "DemandTestPriceChanged").ToArrayAsync();
            Assert.AreEqual(2, audit.Length);
            CollectionAssert.AreEquivalent(new[] { f.Head.UserId, f.Owner.UserId }, audit.Select(item => item.ActorId).ToArray());
            Assert.AreEqual(1, await db.AuditEvents.CountAsync(item => item.ObjectId == group && item.Action == "TargetPurchasePriceChanged"));
            Assert.IsFalse(db.Database.HasPendingModelChanges());
            string comment = await db.Database.SqlQueryRaw<string>("SELECT col_description('collection.search_group_market_settings'::regclass, attnum) AS \"Value\" FROM pg_attribute WHERE attrelid='collection.search_group_market_settings'::regclass AND attname='demand_test_price_per_sotka'").SingleAsync();
            StringAssert.Contains(comment, "Ручная цена теста спроса");
            string targetComment = await db.Database.SqlQueryRaw<string>("SELECT col_description('collection.search_group_market_settings'::regclass, attnum) AS \"Value\" FROM pg_attribute WHERE attrelid='collection.search_group_market_settings'::regclass AND attname='target_purchase_price_per_sotka'").SingleAsync();
            StringAssert.Contains(targetComment, "нужная цена покупки");
        }
        await f.SetExplicitAccessAsync(f.EmployeeId("head-phase1@test.invalid"), EmployeeAccessRules.NoAccess);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => service.SaveDemandAsync(f.Head, new(group, changed.Version, 1m), "m03-revoked", Ct));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => service.ReadIncomingAsync(f.Head, new(new(), SearchGroupId: group), Ct));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => service.ReadProcurementGroupsAsync(f.Head, Ct));
    }

    [TestMethod]
    public async Task ConcurrentFirstDemandWriteHasOneWinner()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        await using var source = NpgsqlDataSource.Create(f.Sandbox.RuntimeConnection);
        var access = new EmployeeAccessService(f.Factory);
        var service = new GroupMarketService(f.Factory, access, new IncomingFilterPresetService(source, access));
        Guid group = Guid.CreateVersion7();
        await using (var db = f.Sandbox.Context())
        {
            db.SearchGroups.Add(new() { Id = group, OrganizationId = f.OrganizationId, Name = "Без настроек", Active = true });
            await db.SaveChangesAsync();
        }
        async Task<bool> Attempt(decimal price)
        {
            try { await service.SaveDemandAsync(f.Head, new(group, 0, price), "m03-race", Ct); return true; }
            catch (DbUpdateConcurrencyException) { return false; }
        }
        bool[] results = await Task.WhenAll(Attempt(10m), Attempt(20m));
        Assert.AreEqual(1, results.Count(item => item));
        await using var check = f.Sandbox.Context();
        Assert.AreEqual(1, await check.SearchGroupMarketSettings.CountAsync(item => item.SearchGroupId == group));
        Assert.AreEqual(1, await check.AuditEvents.CountAsync(item => item.ObjectId == group && item.Action == "DemandTestPriceChanged"));
    }

    [TestMethod]
    public async Task ContextsShareWholeGroupPricesAndCasesUseConfirmedVisibleSourceMembership()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(true, true);
        var pair = await f.IngestMarketplacePairAsync();
        Guid a = await pair.Administration.CreateGroupAsync(f.Owner, "А группа", 0, "m03", Ct);
        Guid b = await pair.Administration.CreateGroupAsync(f.Owner, "Б группа", 1, "m03", Ct);
        await using var source = NpgsqlDataSource.Create(f.Sandbox.RuntimeConnection);
        var access = new EmployeeAccessService(f.Factory);
        var presets = new IncomingFilterPresetService(source, access);
        var service = new GroupMarketService(f.Factory, access, presets);
        await using (var db = f.Sandbox.Context())
        {
            foreach (var search in await db.SearchConfigurations.ToArrayAsync()) search.SearchGroupId = search.Source == CatalogSource.Avito ? a : b;
            foreach (var item in await db.Listings.ToArrayAsync()) item.IncludeInCalculation = true;
            await db.SaveChangesAsync();
        }
        Guid independent = await f.InsertIndependentCaseAsync("Без источника");
        Assert.AreEqual(0, (await service.ReadCaseAsync(f.Manager, independent, Ct)).Count);
        Guid caseId = (await f.Workspace.TakeToWorkAsync(f.Manager, new(pair.AvitoId), "m03-take", Ct)).CaseId;
        var single = await service.ReadCaseAsync(f.Manager, caseId, Ct);
        Assert.AreEqual(a, single.Single().SearchGroupId);
        await service.SaveDemandAsync(f.Head, new(a, single[0].Version, 110000m), "m03-demand", Ct);
        var demandChanged = (await service.ReadProcurementAsync(f.Manager, a, Ct))!;
        await service.SaveTargetPurchaseAsync(f.Head, new(a, demandChanged.Version, 90_000m), "m03-target", Ct);
        var saved = await presets.CreateAsync(f.Manager, new(a, "Узкий фильтр", Criteria(a)), Ct);
        var ungrouped = await presets.CreateAsync(f.Manager, new(null, "Без группы", Criteria()), Ct);
        var request = new IncomingCatalogReadFilter(new("несовпадающий текст", MaxPrice: 1m), SearchGroupId: b,
            WorkingScope: new(IncomingCatalogMode.SavedFilters, saved.Id));
        var incoming = (await service.ReadIncomingAsync(f.Manager, request, Ct))!;
        var queueMarket = (await service.ReadProcurementAsync(f.Manager, a, Ct))!;
        Assert.AreEqual(queueMarket, incoming);
        Assert.AreEqual(1, incoming.ParticipantCount);
        Assert.AreEqual(133333.3333m, incoming.MedianPricePerSotka);
        Assert.AreEqual(110000m, incoming.DemandTestPricePerSotka);
        Assert.AreEqual(90_000m, incoming.TargetPurchasePricePerSotka);
        Assert.IsNull(await service.ReadIncomingAsync(f.Manager, new(new()), Ct));
        Assert.IsNull(await service.ReadIncomingAsync(f.Manager, request with { WorkingScope = new(IncomingCatalogMode.SavedFilters, ungrouped.Id) }, Ct));
        Assert.AreEqual(b, (await service.ReadIncomingAsync(f.Manager, request with { WorkingScope = new(IncomingCatalogMode.SavedFilters, saved.Id, UseDraft: true) }, Ct))!.SearchGroupId);
        var overview = new OverviewService(f.Factory, TimeProvider.System);
        Assert.AreEqual(queueMarket, (await overview.ReadMarketGroupsAsync(f.Manager, new(), Ct)).Items.Single(item => item.SearchGroupId == a).Market);
        Assert.AreEqual(queueMarket, (await service.ReadCaseAsync(f.Manager, caseId, Ct)).Single());
        var queues = new ProcurementQueueV2ReadService(f.Factory, TimeProvider.System);
        Assert.AreEqual(1, (await queues.ReadPageAsync(f.Manager, new(SearchGroupId: a), Ct)).Total);
        Assert.AreEqual(0, (await queues.ReadPageAsync(f.Manager, new(SearchGroupId: b), Ct)).Total);
        await using (var db = f.Sandbox.Context())
        {
            db.PropertyCaseSourceLinks.Add(new() { Id = Guid.CreateVersion7(), OrganizationId = f.OrganizationId,
                PropertyCaseId = caseId, CatalogItemId = pair.CianId, Confirmed = false,
                Provenance = "M03 test", RecordedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        Assert.AreEqual(1, (await service.ReadCaseAsync(f.Manager, caseId, Ct)).Count);
        Assert.AreEqual(0, (await queues.ReadPageAsync(f.Manager, new(SearchGroupId: b), Ct)).Total);
        await using (var db = f.Sandbox.Context())
        {
            (await db.PropertyCaseSourceLinks.SingleAsync(item => item.CatalogItemId == pair.CianId)).Confirmed = true;
            var job = await db.CollectionJobs.SingleAsync(item => db.SearchConfigurations.Any(search => search.Id == item.SearchId && search.SearchGroupId == a));
            for (int i = 0; i < 2; i++) db.ListingObservations.Add(new() { Id = Guid.CreateVersion7(), ListingId = pair.CianId,
                AgentId = pair.Agent.AgentId, JobId = job.Id, ObservationKey = "m03-" + i, ContentHash = "m03-" + i,
                PayloadJson = "{}", ChangesJson = "[]", ObservedAt = DateTimeOffset.UtcNow, RecordedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        GroupMarketView[] caseMarkets = (await service.ReadCaseAsync(f.Manager, caseId, Ct)).ToArray();
        CollectionAssert.AreEqual(new[] { a, b }, caseMarkets.Select(item => item.SearchGroupId).ToArray());
        Assert.AreEqual(90_000m, caseMarkets.Single(item => item.SearchGroupId == a).TargetPurchasePricePerSotka);
        Assert.AreEqual(1, (await queues.ReadPageAsync(f.Manager, new(SearchGroupId: a), Ct)).Total);
        Assert.AreEqual(1, (await queues.ReadPageAsync(f.Manager, new(SearchGroupId: b), Ct)).Total);
        Assert.AreEqual(0, (await queues.ReadPageAsync(f.SecondManager, new(SearchGroupId: a), Ct)).Total);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => service.ReadCaseAsync(f.SecondManager, caseId, Ct));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => service.ReadCaseAsync(f.ForeignOwner, caseId, Ct));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => queues.ReadPageAsync(f.ForeignOwner, new(SearchGroupId: a), Ct));
        await f.SetExplicitAccessAsync(f.ManagerEmployeeId, ProcurementTestsHelper.ProcurementManagerAccess(AccessScope.Department) with { IncomingAccess = IncomingAccessLevel.None });
        Assert.IsFalse((await service.ReadCaseAsync(f.Manager, caseId, Ct))[0].CanReadParticipants);
        Assert.AreEqual(2, (await service.ReadProcurementAsync(f.Manager, a, Ct))!.ParticipantCount);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => service.ReadIncomingAsync(f.Manager, request, Ct));
    }
}
