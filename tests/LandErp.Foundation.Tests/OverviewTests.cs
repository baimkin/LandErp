using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Overview.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Infrastructure.Modules.Overview;
using LandErp.Infrastructure.Modules.Procurement;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
[TestCategory("PostgreSQL")]
public sealed class OverviewTests
{
    [TestMethod]
    public async Task MarketStatisticsAreServerSideDistinctStatusIndependentAndConfiguredPerGroup()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(
            includeSecondManager: false, includeTeams: false);
        var marketplace = await fixture.IngestMarketplacePairAsync();
        Guid primaryGroup = await marketplace.Administration.CreateGroupAsync(
            fixture.Owner, "Север Московской области", 10, "overview-market", CancellationToken.None);
        Guid independentGroup = await marketplace.Administration.CreateGroupAsync(
            fixture.Owner, "Юг Московской области", 20, "overview-market", CancellationToken.None);

        await using (LandErpDbContext db = await fixture.Factory.CreateDbContextAsync())
        {
            foreach (var search in await db.SearchConfigurations.ToArrayAsync()) search.SearchGroupId = primaryGroup;
            Listing avito = await db.Listings.SingleAsync(item => item.Id == marketplace.AvitoId);
            avito.Title = "Участок ИЖС";
            avito.Disposition = CatalogDisposition.Dismissed;
            Listing cian = await db.Listings.SingleAsync(item => item.Id == marketplace.CianId);
            cian.Title = "Участок ИЖС";
            cian.Disposition = CatalogDisposition.Fake;
            await db.SaveChangesAsync();
        }
        await fixture.IngestChangedAvitoAsync(marketplace.Agent, marketplace.Administration, 3_000_000m);

        OverviewService service = new(fixture.Factory, fixture.Access, TimeProvider.System);
        MarketGroupPage initial = await service.ReadMarketGroupsAsync(fixture.Owner, new(Size: 20), CancellationToken.None);
        MarketGroupRow primary = initial.Items.Single(item => item.SearchGroupId == primaryGroup);
        Assert.AreEqual(1, primary.IncludedCount, "A listing observed more than once must be counted once.");
        Assert.AreEqual(0, primary.ExcludedCount);
        Assert.AreEqual(1, primary.FakeExcludedCount, "Fake is a separate invalid-data exclusion.");
        Assert.AreEqual(200_000m, primary.MedianPricePerSotka);
        Assert.AreEqual(200_000m, primary.AveragePricePerSotka);

        SearchGroupMarketSettingsView saved = await service.SaveMarketSettingsAsync(fixture.Owner,
            new(primaryGroup, primary.Settings.Version, 7, [IncomingLandType.Snt], 100_000m, 250_000m),
            "overview-market-filter", CancellationToken.None);
        MarketGroupPage filtered = await service.ReadMarketGroupsAsync(fixture.Owner, new(Size: 20), CancellationToken.None);
        primary = filtered.Items.Single(item => item.SearchGroupId == primaryGroup);
        MarketGroupRow independent = filtered.Items.Single(item => item.SearchGroupId == independentGroup);
        Assert.AreEqual(0, primary.IncludedCount);
        Assert.AreEqual(1, primary.ExcludedCount, "The valid listing is excluded by this group's type filter.");
        Assert.AreEqual(1, primary.FakeExcludedCount);
        Assert.AreEqual(7, saved.PeriodDays);
        Assert.AreEqual(30, independent.Settings.PeriodDays, "Saving one group must not change another group.");
        Assert.IsTrue(independent.Settings.AllowedPropertyTypes.Contains(IncomingLandType.Izhs));

        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => service.SaveMarketSettingsAsync(fixture.Manager,
            new(primaryGroup, saved.Version, 30, [IncomingLandType.Izhs], null, null),
            "overview-market-denied", CancellationToken.None));
        MarketGroupPage foreign = await service.ReadMarketGroupsAsync(fixture.ForeignOwner, new(Size: 20), CancellationToken.None);
        Assert.AreEqual(0, foreign.Total, "Search groups from another organization must be invisible.");

        for (int index = 0; index < 4; index++)
            await marketplace.Administration.CreateGroupAsync(fixture.Owner, $"Дополнительная группа {index}", 30 + index,
                "overview-market-bound", CancellationToken.None);
        OverviewView overview = await service.ReadAsync(fixture.Owner, CancellationToken.None);
        Assert.AreEqual(6, overview.Market.Total);
        Assert.AreEqual(5, overview.Market.Items.Count, "The compact dashboard projection must stay bounded.");
    }

    [TestMethod]
    public async Task ProcurementMetricsRowsAndTeamWorkloadUseTheSameAccessScope()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(
            includeSecondManager: true, includeTeams: true);
        await fixture.ChangeScopeAsync("manager-phase1@test.invalid", AccessScope.Team, fixture.TeamA);
        await fixture.ChangeScopeAsync("manager2-phase1@test.invalid", AccessScope.Team, fixture.TeamB);
        Guid first = await fixture.InsertIndependentCaseAsync("Объект команды А");
        Guid second = await fixture.InsertIndependentCaseAsync("Объект команды Б");
        Guid secondEmployeeId = fixture.EmployeeId("manager2-phase1@test.invalid");

        await using (LandErpDbContext db = await fixture.Factory.CreateDbContextAsync())
        {
            PropertyCase firstCase = await db.PropertyCases.SingleAsync(item => item.Id == first);
            firstCase.TeamId = fixture.TeamA;
            PropertyCase secondCase = await db.PropertyCases.SingleAsync(item => item.Id == second);
            secondCase.DepartmentId = fixture.DepartmentB;
            secondCase.TeamId = fixture.TeamB;
            secondCase.ManagerEmployeeId = secondEmployeeId;
            (await db.WorkAssignments.SingleAsync(item => item.Id == secondCase.AssignmentId)).EmployeeId = secondEmployeeId;
            (await db.WorkTasks.SingleAsync(item => item.Id == secondCase.WorkTaskId)).EmployeeId = secondEmployeeId;
            await db.SaveChangesAsync();
        }

        OverviewService service = new(fixture.Factory, fixture.Access, TimeProvider.System);
        OverviewView firstOverview = await service.ReadAsync(fixture.Manager, CancellationToken.None);
        OverviewView secondOverview = await service.ReadAsync(fixture.SecondManager, CancellationToken.None);
        OverviewView foreignOverview = await service.ReadAsync(fixture.ForeignOwner, CancellationToken.None);
        ProcurementQueueV2ReadService queue = new(fixture.Factory, fixture.Access, TimeProvider.System);
        ProcurementQueueV2Page ownerQueue = await queue.ReadPageAsync(fixture.Owner, new(), CancellationToken.None);
        ProcurementQueueV2Page ownerMine = await queue.ReadPageAsync(fixture.Owner, new(MineOnly: true), CancellationToken.None);
        ProcurementQueueV2Page managerMine = await queue.ReadPageAsync(fixture.Manager, new(MineOnly: true), CancellationToken.None);

        Assert.AreEqual(1, firstOverview.ActiveProcurement.Value);
        Assert.AreEqual(1, secondOverview.ActiveProcurement.Value);
        Assert.AreEqual(1, firstOverview.MyWorkTotal);
        Assert.AreEqual(1, secondOverview.MyWorkTotal);
        Assert.AreEqual(1, firstOverview.TeamTotal);
        Assert.AreEqual(1, secondOverview.TeamTotal);
        Assert.AreEqual(0, foreignOverview.ActiveProcurement.Value);
        Assert.AreEqual(0, foreignOverview.MyWorkTotal);
        Assert.AreEqual(0, foreignOverview.TeamTotal);
        Assert.AreEqual(2, ownerQueue.Total);
        Assert.AreEqual(0, ownerMine.Total);
        Assert.AreEqual(1, managerMine.Total);
    }
}
