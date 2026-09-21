using System.Text;
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
    public async Task NegotiationHistoryInspectionReportAndAttachmentsStayCaseScoped()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(
            includeSecondManager: false, includeTeams: false);
        Guid caseId = await fixture.InsertIndependentCaseAsync("Объект с переговорами и осмотром");
        Guid negotiationWithFile = Guid.Empty;
        for (int index = 0; index < 4; index++)
        {
            CaseCard card = await fixture.Workspace.ReadCardAsync(fixture.Manager, caseId, CancellationToken.None);
            Guid id = await fixture.Workspace.AddNegotiationWithIdAsync(fixture.Manager,
                new(caseId, card.Item.CaseVersion, 5_000_000m - index * 100_000m, 4_500_000m + index * 50_000m,
                    index == 3 ? 4_700_000m : null, index % 2 == 0 ? "Телефон" : "Встреча", "Собственник",
                    $"Контакт {index + 1}", "Без дополнительных условий", $"Комментарий {index + 1}",
                    "Получить документы", DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddMinutes(-10 + index)),
                $"history-{index}", CancellationToken.None);
            if (index == 1) negotiationWithFile = id;
        }

        byte[] content = Encoding.UTF8.GetBytes("procurement-v2-material");
        await fixture.Workspace.AddAttachmentAsync(fixture.Manager,
            new(caseId, CaseAttachmentOwner.Negotiation, negotiationWithFile, CaseAttachmentKind.Photo,
                "Фото от собственника", "Материал к контакту", "owner.jpg", "image/jpeg", content, null),
            "negotiation-file", CancellationToken.None);

        Guid inspectionId = await fixture.Workspace.SaveInspectionAsync(fixture.Manager,
            new(caseId, null, null, "", "", [], false), "start-inspection", CancellationToken.None);
        CaseCard inspectionCard = await fixture.Workspace.ReadCardAsync(fixture.Manager, caseId, CancellationToken.None);
        InspectionItemView problemItem = inspectionCard.Inspection!.Items.First(item => item.NormalAnswer.Length > 0);
        string abnormalAnswer = string.Equals(problemItem.NormalAnswer, "Нет", StringComparison.OrdinalIgnoreCase) ? "Да" : "Нет";
        await fixture.Workspace.SaveInspectionAsync(fixture.Manager,
            new(caseId, inspectionId, inspectionCard.Inspection.Version, "Нужна дополнительная проверка",
                "Вернуться к вопросу после документов",
                [new(problemItem.Id, problemItem.Version, InspectionItemStatus.Answered, abnormalAnswer, "Есть замечание")], false),
            "save-inspection", CancellationToken.None);
        await fixture.Workspace.AddAttachmentAsync(fixture.Manager,
            new(caseId, CaseAttachmentOwner.Inspection, inspectionId, CaseAttachmentKind.Photo,
                "Общий вид", "", "view.jpg", "image/jpeg", content, null), "inspection-photo", CancellationToken.None);
        await fixture.Workspace.AddAttachmentAsync(fixture.Manager,
            new(caseId, CaseAttachmentOwner.InspectionItem, problemItem.Id, CaseAttachmentKind.Audio,
                "Комментарий инспектора", "", "note.mp3", "audio/mpeg", content, null), "inspection-audio", CancellationToken.None);
        await fixture.Workspace.AddAttachmentAsync(fixture.Manager,
            new(caseId, CaseAttachmentOwner.Inspection, inspectionId, CaseAttachmentKind.Document,
                "Схема участка", "", "scheme.pdf", "application/pdf", content, null), "inspection-document", CancellationToken.None);

        ProcurementQueueV2ReadService service = new(fixture.Factory, TimeProvider.System);
        ProcurementQueueV2Detail detail = await service.ReadDetailAsync(fixture.Manager, caseId, CancellationToken.None);
        Assert.AreEqual(3, detail.Negotiations.Count, "Drawer remains a short preview.");
        Assert.AreEqual(3, detail.Inspection.MaterialCount);
        Assert.AreEqual(1, detail.Inspection.PhotoVideoCount);
        Assert.AreEqual(1, detail.Inspection.AudioCount);
        Assert.AreEqual(1, detail.Inspection.FileCount);

        ProcurementNegotiationHistoryPage firstPage = await service.ReadNegotiationsAsync(
            fixture.Manager, caseId, 0, 2, CancellationToken.None);
        Assert.AreEqual(4, firstPage.Total);
        Assert.AreEqual(2, firstPage.Items.Count);
        ProcurementNegotiationHistoryPage all = await service.ReadNegotiationsAsync(
            fixture.Manager, caseId, 0, 100, CancellationToken.None);
        Assert.AreEqual(1, all.Items.Single(item => item.Id == negotiationWithFile).Attachments.Count);
        Assert.AreEqual("Фото от собственника", all.Items.Single(item => item.Id == negotiationWithFile).Attachments.Single().Label);

        ProcurementInspectionReport report = (await service.ReadInspectionReportAsync(
            fixture.Manager, caseId, CancellationToken.None))!;
        Assert.AreEqual(inspectionId, report.InspectionId);
        Assert.AreEqual("Нужна дополнительная проверка", report.OverallConclusion);
        Assert.AreEqual(2, report.Attachments.Count);
        ProcurementInspectionReportItem problem = report.Items.Single(item => item.Id == problemItem.Id);
        Assert.IsTrue(problem.Problem);
        Assert.AreEqual(1, problem.Attachments.Count);
        Assert.IsFalse(typeof(ProcurementQueueV2Attachment).GetProperties()
            .Any(property => property.Name.Contains("StorageKey", StringComparison.Ordinal)));

        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => service.ReadNegotiationsAsync(
            fixture.ForeignOwner, caseId, 0, 20, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => service.ReadInspectionReportAsync(
            fixture.ForeignOwner, caseId, CancellationToken.None));
    }

    [TestMethod]
    public async Task NextActionPersistsProjectsAndWritesTimelineWithoutChangingCaseScope()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(includeSecondManager: true, includeTeams: false);
        Guid caseId = await fixture.InsertIndependentCaseAsync("Участок для следующего действия");
        ProcurementQueueV2ReadService service = new(fixture.Factory, TimeProvider.System);
        ProcurementQueueV2Detail before = await service.ReadDetailAsync(fixture.Manager, caseId, CancellationToken.None);
        Guid secondManagerId = fixture.EmployeeId("manager2-phase1@test.invalid");
        DateTimeOffset due = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.AddDays(3).ToUnixTimeMilliseconds());

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

        ProcurementQueueV2ReadService service = new(fixture.Factory, TimeProvider.System);
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
        ProcurementQueueV2ReadService service = new(fixture.Factory, TimeProvider.System);
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
        ProcurementQueueV2ReadService service = new(fixture.Factory, TimeProvider.System);

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
