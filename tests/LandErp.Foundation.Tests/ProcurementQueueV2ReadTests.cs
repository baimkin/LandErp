using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Application.Modules.Workflow.Domain;
using LandErp.Infrastructure.Modules.Procurement;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
[TestCategory("PostgreSQL")]
public sealed class ProcurementQueueV2ReadTests
{
    [TestMethod]
    public async Task NextActionPersistsProjectsAndWritesTimelineWithoutChangingCaseScope()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(includeSecondManager: true, includeTeams: false);
        Guid caseId = await fixture.InsertIndependentCaseAsync("Участок для следующего действия");
        ProcurementQueueV2ReadService service = new(fixture.Factory, fixture.Access, TimeProvider.System);
        ProcurementQueueV2Detail before = await service.ReadDetailAsync(fixture.Manager, caseId, CancellationToken.None);
        Guid secondManagerId = fixture.EmployeeId("manager2-phase1@test.invalid");
        DateTimeOffset due = DateTimeOffset.UtcNow.AddDays(3);

        await fixture.Workspace.SaveNextActionAsync(fixture.Manager,
            new SaveNextAction(caseId, before.CaseVersion, before.TaskVersion, WorkTaskType.Call,
                "Позвонить собственнику", "Согласовать встречу и уточнить условия торга", due, secondManagerId),
            "next-action-test", CancellationToken.None);

        ProcurementQueueV2Detail detail = await service.ReadDetailAsync(fixture.Manager, caseId, CancellationToken.None);
        Assert.AreEqual(WorkTaskType.Call, detail.NextActionType);
        Assert.AreEqual("Позвонить собственнику", detail.NextActionTitle);
        Assert.AreEqual("Согласовать встречу и уточнить условия торга", detail.NextActionDescription);
        Assert.AreEqual(secondManagerId, detail.AssigneeId);
        Assert.AreEqual(due, detail.DueAt);
        Assert.IsTrue(detail.TaskVersion > before.TaskVersion);
        Assert.IsTrue(detail.CaseVersion > before.CaseVersion);
        Assert.IsTrue(detail.Timeline.Any(item => item.Kind == "NextActionChanged" && item.DueAt == due));

        ProcurementQueueV2Row row = (await service.ReadPageAsync(fixture.Manager,
            new ProcurementQueueV2Filter(AssigneeId: secondManagerId), CancellationToken.None)).Items.Single();
        Assert.AreEqual(WorkTaskType.Call, row.NextActionType);
        Assert.AreEqual(detail.NextActionDescription, row.NextActionDescription);
        await using (LandErpDbContext db = await fixture.Factory.CreateDbContextAsync())
        {
            Assert.AreEqual(fixture.ManagerEmployeeId, await db.WorkAssignments.Where(item => item.ObjectId == caseId)
                .Select(item => item.EmployeeId).SingleAsync(), "Changing the action assignee must not redefine PropertyCase visibility.");
            Assert.AreEqual(1, await db.AuditEvents.CountAsync(item => item.ObjectId == caseId && item.Action == "ProcurementNextActionChanged"));
        }

        await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(() => fixture.Workspace.SaveNextActionAsync(fixture.Manager,
            new SaveNextAction(caseId, before.CaseVersion, before.TaskVersion, WorkTaskType.Check,
                "Устаревшая команда", "Эта команда не должна сохраниться", due, fixture.ManagerEmployeeId),
            "next-action-stale", CancellationToken.None));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => fixture.Workspace.SaveNextActionAsync(fixture.ForeignOwner,
            new SaveNextAction(caseId, detail.CaseVersion, detail.TaskVersion, WorkTaskType.Check,
                "Чужая команда", "Другая организация не должна получить доступ", due, fixture.ManagerEmployeeId),
            "next-action-foreign", CancellationToken.None));
    }

    [TestMethod]
    public async Task ProjectionSearchesFiltersAndPagesServerSide()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(includeSecondManager: false, includeTeams: false);
        Guid firstId = await fixture.InsertIndependentCaseAsync("Участок у станции");
        Guid secondId = await fixture.InsertIndependentCaseAsync("Участок у леса");
        await using (LandErpDbContext db = await fixture.Factory.CreateDbContextAsync())
        {
            PropertyCase first = await db.PropertyCases.SingleAsync(item => item.Id == firstId);
            first.BusinessNumber = "PC-V2-001";
            first.CadastralNumber = "50:10:1234567:89";
            first.WorkingLocation = "Химки, Фирсановка";
            first.WorkingPrice = 12_500_000m;
            first.WorkingAreaSquareMeters = 1_500m;
            PropertyCase second = await db.PropertyCases.SingleAsync(item => item.Id == secondId);
            second.BusinessNumber = "PC-V2-002";
            second.CadastralNumber = "50:11:7654321:10";
            second.WorkingLocation = "Солнечногорск";
            var task = await db.WorkTasks.SingleAsync(item => item.ObjectId == firstId);
            task.DueAt = DateTimeOffset.UtcNow.AddDays(-2);
            db.CaseChecks.Add(new()
            {
                Id = Guid.NewGuid(),
                OrganizationId = fixture.OrganizationId,
                PropertyCaseId = firstId,
                Level = CaseCheckLevel.Quick,
                Title = "Кадастровая проверка",
                Status = CaseCheckStatus.Issue,
                AuthorEmployeeId = fixture.ManagerEmployeeId,
                RecordedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        ProcurementQueueV2ReadService service = new(fixture.Factory, fixture.Access, TimeProvider.System);
        ProcurementQueueV2Page cadastral = await service.ReadPageAsync(fixture.Manager,
            new ProcurementQueueV2Filter(Text: "50:10:1234567:89", Size: 1), CancellationToken.None);
        Assert.AreEqual(1, cadastral.Total);
        Assert.AreEqual(firstId, cadastral.Items.Single().CaseId);
        Assert.AreEqual(12_500_000m, cadastral.Items.Single().WorkingPrice);
        Assert.AreEqual(1_500m, cadastral.Items.Single().AreaSquareMeters);

        ProcurementQueueV2Page filtered = await service.ReadPageAsync(fixture.Manager,
            new ProcurementQueueV2Filter(OverdueOnly: true, Checks: ProcurementQueueV2CheckFilter.HasIssues), CancellationToken.None);
        Assert.AreEqual(1, filtered.Total);
        Assert.AreEqual(firstId, filtered.Items.Single().CaseId);
        Assert.AreEqual(1, filtered.Items.Single().QuickChecks.Issues);

        ProcurementQueueV2Page firstPage = await service.ReadPageAsync(fixture.Manager,
            new ProcurementQueueV2Filter(Text: "Участок", Sort: ProcurementQueueV2Sort.RecordedAt, Descending: false, Size: 1), CancellationToken.None);
        ProcurementQueueV2Page secondPage = await service.ReadPageAsync(fixture.Manager,
            new ProcurementQueueV2Filter(Text: "Участок", Sort: ProcurementQueueV2Sort.RecordedAt, Descending: false, Offset: 1, Size: 1), CancellationToken.None);
        Assert.AreEqual(2, firstPage.Total);
        Assert.AreEqual(1, firstPage.Items.Count);
        Assert.AreEqual(1, secondPage.Items.Count);
        Assert.AreNotEqual(firstPage.Items[0].CaseId, secondPage.Items[0].CaseId);
    }

    [TestMethod]
    public async Task SourceSearchAndChangedFilterKeepPropertyCaseWorkingFactsIndependent()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(includeSecondManager: false, includeTeams: false);
        var marketplace = await fixture.IngestMarketplacePairAsync();
        Guid avitoId = marketplace.AvitoId;
        TakeToWorkResult taken = await fixture.Workspace.TakeToWorkAsync(fixture.Manager, new(avitoId), "v2-take", CancellationToken.None);
        PropertyCase before = await fixture.ReadCaseAsync(taken.CaseId);

        await fixture.IngestChangedAvitoAsync(marketplace.Agent, marketplace.Administration, 1_700_000m);
        await using (LandErpDbContext db = await fixture.Factory.CreateDbContextAsync())
        {
            Listing source = await db.Listings.SingleAsync(item => item.Id == avitoId);
            source.SellerName = "Иван Иванов";
            await db.SaveChangesAsync();
        }
        ProcurementQueueV2ReadService service = new(fixture.Factory, fixture.Access, TimeProvider.System);
        ProcurementQueueV2Page page = await service.ReadPageAsync(fixture.Manager,
            new ProcurementQueueV2Filter(Text: "10001", Source: CatalogSource.Avito, SourceChangedOnly: true), CancellationToken.None);

        Assert.AreEqual(1, page.Total);
        ProcurementQueueV2Row row = page.Items.Single();
        Assert.AreEqual(taken.CaseId, row.CaseId);
        Assert.IsTrue(row.SourceChanged);
        Assert.AreEqual(before.WorkingPrice, row.WorkingPrice, "Source revision must not become a working fact without ApplySourceFact.");
        Assert.IsTrue(row.Sources.Any(item => item.Source == CatalogSource.Avito));
        Assert.AreEqual(1, page.Summary.Checking);
        Assert.AreEqual(0, page.Summary.PendingHead);
        Assert.AreEqual(1, page.Summary.PriceChanged);

        ProcurementQueueV2Page changedPrice = await service.ReadPageAsync(fixture.Manager,
            new ProcurementQueueV2Filter(MineOnly: true, PriceChangedOnly: true), CancellationToken.None);
        Assert.AreEqual(1, changedPrice.Total, "MineOnly and PriceChangedOnly must compose without weakening scope.");
        Assert.AreEqual(taken.CaseId, changedPrice.Items.Single().CaseId);
        Assert.AreEqual(1, (await service.ReadPageAsync(fixture.Manager,
            new ProcurementQueueV2Filter(Text: "Иван Иванов"), CancellationToken.None)).Total);
        Assert.AreEqual(1, (await service.ReadPageAsync(fixture.Manager,
            new ProcurementQueueV2Filter(Text: taken.CaseId.ToString()), CancellationToken.None)).Total);

        ProcurementQueueV2Detail detail = await service.ReadDetailAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        Assert.AreEqual(before.WorkingPrice, detail.WorkingPrice);
        Assert.AreEqual(2_000_000m, detail.StartPrice);
        Assert.AreEqual(-300_000m, detail.PriceDeltaFromStart);
        Assert.AreEqual(-15m, detail.PriceDeltaFromStartPercent);
        Assert.IsTrue(detail.PriceChanged);
        ProcurementSourceDetail avito = detail.Sources.Single(item => item.CatalogItemId == avitoId);
        Assert.IsTrue(avito.Changed);
        Assert.IsTrue(avito.ApplicableFacts.Contains(CaseFactField.Price));
    }

    [TestMethod]
    public async Task ProjectionUsesCaseResponsibilityScopeNotListingRouting()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(includeSecondManager: true, includeTeams: true);
        await fixture.ChangeScopeAsync("manager-phase1@test.invalid", AccessScope.Team, fixture.TeamA);
        await fixture.ChangeScopeAsync("manager2-phase1@test.invalid", AccessScope.Team, fixture.TeamB);
        Guid itemId = await fixture.CreateUnlinkedManualAsync();
        TakeToWorkResult taken = await fixture.Workspace.TakeToWorkAsync(fixture.Manager, new(itemId), "v2-scope", CancellationToken.None);
        ProcurementQueueV2ReadService service = new(fixture.Factory, fixture.Access, TimeProvider.System);

        Assert.AreEqual(1, (await service.ReadPageAsync(fixture.Manager, new(), CancellationToken.None)).Total);
        Assert.AreEqual(1, (await fixture.Workspace.ReadQueueAsync(fixture.Manager, new(), CancellationToken.None)).Total);
        Assert.AreEqual(0, (await service.ReadPageAsync(fixture.SecondManager, new(), CancellationToken.None)).Total);

        await using (LandErpDbContext db = await fixture.Factory.CreateDbContextAsync())
        {
            Listing source = await db.Listings.SingleAsync(item => item.Id == itemId);
            source.DepartmentId = fixture.DepartmentB;
            source.TeamId = fixture.TeamB;
            await db.SaveChangesAsync();
        }

        Assert.AreEqual(1, (await service.ReadPageAsync(fixture.Manager, new(), CancellationToken.None)).Total,
            "Changing source routing metadata must not change PropertyCase visibility.");
        Assert.AreEqual(0, (await service.ReadPageAsync(fixture.SecondManager, new(), CancellationToken.None)).Total);
        foreach (AccessScope scope in new[] { AccessScope.Department, AccessScope.Own, AccessScope.AssignedObjects })
        {
            await fixture.ChangeScopeAsync("manager-phase1@test.invalid", scope, null);
            Assert.AreEqual(1, (await service.ReadPageAsync(fixture.Manager, new(), CancellationToken.None)).Total,
                $"Queue V2 must preserve {scope} visibility.");
            Assert.AreEqual(1, (await fixture.Workspace.ReadQueueAsync(fixture.Manager, new(), CancellationToken.None)).Total,
                $"Workspace and Queue V2 must share {scope} visibility.");
        }
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => service.ReadDetailAsync(fixture.SecondManager, taken.CaseId, CancellationToken.None));
    }
}
