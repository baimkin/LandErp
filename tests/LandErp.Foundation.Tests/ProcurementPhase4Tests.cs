using System.Text;
using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
[TestCategory("PostgreSQL")]
public sealed class ProcurementPhase4Tests
{
    private static readonly string[] ReviewActions = ["Forward", "Return", "Approve"];
    private static readonly string[] ExpectedReviewActions = ["Forward", "Return", "Forward", "Approve"];

    [TestMethod]
    public async Task NegotiationPriceTypesRemainIndependentAndAgreedIsNotAcquired()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        Guid itemId = await fixture.Workspace.CreateManualAsync(fixture.Manager,
            new(CatalogSource.Telegram, "Переговорный объект", "Химки", 19_000_000m, 840m, null, null, null, null, "Phase 4"),
            "phase4", CancellationToken.None);
        TakeToWorkResult taken = await fixture.Workspace.TakeToWorkAsync(fixture.Manager, new(itemId), "take", CancellationToken.None);
        (NegotiationPriceType Type, decimal Amount)[] prices =
        [
            (NegotiationPriceType.Ask, 19_000_000m),
            (NegotiationPriceType.SellerOffer, 16_600_000m),
            (NegotiationPriceType.BuyerOffer, 16_000_000m),
            (NegotiationPriceType.Agreed, 16_300_000m)
        ];
        foreach ((NegotiationPriceType type, decimal amount) in prices)
        {
            CaseCard current = await fixture.Workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
            await fixture.Workspace.AddNegotiationAsync(fixture.Manager,
                new(taken.CaseId, current.Item.CaseVersion, type, amount, "Звонок", "Собственник", "Диалог продолжается",
                    "Без дополнительных условий", "Рабочий контакт", "Перезвонить", DateTimeOffset.UtcNow.AddMinutes(-1)),
                "negotiation", CancellationToken.None);
        }

        CaseCard card = await fixture.Workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        Assert.AreEqual(4, card.Negotiations.Count);
        foreach ((NegotiationPriceType type, decimal amount) in prices)
            Assert.AreEqual(amount, card.Negotiations.Single(item => item.PriceType == type).Amount);
        Assert.AreEqual(19_000_000m, card.Item.Price, "Negotiation prices must not overwrite the case working price.");
        Assert.AreNotEqual("acquired", card.Item.Stage, "Agreed negotiation is not an acquisition fact.");
    }

    [TestMethod]
    public async Task ReturnReworkResubmitPreservesTaskHistoryAndEnablesStructuredChecks()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        Guid itemId = await fixture.CreateUnlinkedManualAsync();
        TakeToWorkResult taken = await fixture.Workspace.TakeToWorkAsync(fixture.Manager, new(itemId), "take", CancellationToken.None);
        Guid taskId; Guid headEmployee = fixture.EmployeeId("head-phase1@test.invalid");
        await using (LandErpDbContext db = await fixture.Factory.CreateDbContextAsync())
            taskId = await db.PropertyCases.Where(item => item.Id == taken.CaseId).Select(item => item.WorkTaskId).SingleAsync();

        CaseCard card = await fixture.Workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        await fixture.Workspace.SaveCheckAsync(fixture.Manager,
            new(taken.CaseId, null, card.Item.CaseVersion, null, CaseCheckLevel.Quick, "Сверить кадастровую карту",
                CaseCheckStatus.Passed, fixture.ManagerEmployeeId, null, null, "Границы совпадают", false), "quick", CancellationToken.None);
        card = await fixture.Workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        CheckView quick = card.Checks.Single();
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => fixture.Workspace.SaveCheckAsync(fixture.Manager,
            new(taken.CaseId, quick.Id, card.Item.CaseVersion, quick.Version, quick.Level, quick.Title,
                CaseCheckStatus.Blocked, fixture.ManagerEmployeeId, null, null, "Найден риск", true), "blocker-denied", CancellationToken.None));

        await fixture.Workspace.DecideAsync(fixture.Manager, new(taken.CaseId, card.Item.CaseVersion, card.Item.SourceRevision,
            ProcurementAction.Forward, "Первичный анализ готов", "", headEmployee, null), "forward", CancellationToken.None);
        card = await fixture.Workspace.ReadCardAsync(fixture.Head, taken.CaseId, CancellationToken.None);
        await fixture.Workspace.DecideAsync(fixture.Head, new(taken.CaseId, card.Item.CaseVersion, card.Item.SourceRevision,
            ProcurementAction.Return, "Нужен документ на подъезд", "Получить схему сервитута", fixture.ManagerEmployeeId, null), "return", CancellationToken.None);
        card = await fixture.Workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        await fixture.Workspace.AddNoteAsync(fixture.Manager,
            new(taken.CaseId, card.Item.CaseVersion, "Схема подъезда получена", false, "", null), "rework", CancellationToken.None);
        card = await fixture.Workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        await fixture.Workspace.DecideAsync(fixture.Manager, new(taken.CaseId, card.Item.CaseVersion, card.Item.SourceRevision,
            ProcurementAction.Forward, "Замечание исправлено", "", headEmployee, null), "resubmit", CancellationToken.None);
        card = await fixture.Workspace.ReadCardAsync(fixture.Head, taken.CaseId, CancellationToken.None);
        await fixture.Workspace.DecideAsync(fixture.Head, new(taken.CaseId, card.Item.CaseVersion, card.Item.SourceRevision,
            ProcurementAction.Approve, "Можно продолжать переговоры и проверки", "", null, null), "approve", CancellationToken.None);

        card = await fixture.Workspace.ReadCardAsync(fixture.Head, taken.CaseId, CancellationToken.None);
        await fixture.Workspace.SaveCheckAsync(fixture.Head,
            new(taken.CaseId, quick.Id, card.Item.CaseVersion, quick.Version, quick.Level, quick.Title,
                CaseCheckStatus.Blocked, fixture.ManagerEmployeeId, null, null, "Проезд требует юридического подтверждения", true),
            "blocker", CancellationToken.None);
        card = await fixture.Workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        await fixture.Workspace.SaveCheckAsync(fixture.Manager,
            new(taken.CaseId, null, card.Item.CaseVersion, null, CaseCheckLevel.Deep, "Юридическая проверка права",
                CaseCheckStatus.InProgress, fixture.ManagerEmployeeId, DateTimeOffset.UtcNow.AddDays(3), 20_000m,
                "Запрошена выписка и документы-основания", false), "deep", CancellationToken.None);

        await using (LandErpDbContext db = await fixture.Factory.CreateDbContextAsync())
        {
            Assert.AreEqual(taskId, await db.PropertyCases.Where(item => item.Id == taken.CaseId).Select(item => item.WorkTaskId).SingleAsync());
            CollectionAssert.AreEqual(ExpectedReviewActions,
                await db.WorkflowTransitions.Where(item => item.ObjectId == taken.CaseId && ReviewActions.Contains(item.Action))
                    .OrderBy(item => item.RecordedAt).Select(item => item.Action).ToArrayAsync());
            Assert.AreEqual("negotiation", await db.PropertyCases.Where(item => item.Id == taken.CaseId).Select(item => item.StageId).SingleAsync());
            Assert.AreEqual(2, await db.CaseChecks.CountAsync(item => item.PropertyCaseId == taken.CaseId));
        }
    }

    [TestMethod]
    public async Task AttachmentsFollowOwningCaseScopeAndHideStorageKey()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(true, true);
        await fixture.ChangeScopeAsync("manager-phase1@test.invalid", AccessScope.Team, fixture.TeamA);
        await fixture.ChangeScopeAsync("manager2-phase1@test.invalid", AccessScope.Team, fixture.TeamB);
        Guid itemId = await fixture.CreateUnlinkedManualAsync();
        TakeToWorkResult taken = await fixture.Workspace.TakeToWorkAsync(fixture.Manager, new(itemId), "take", CancellationToken.None);
        CaseCard card = await fixture.Workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        await fixture.Workspace.AddNegotiationAsync(fixture.Manager,
            new(taken.CaseId, card.Item.CaseVersion, NegotiationPriceType.SellerOffer, 2_000_000m, "Сообщение", "Продавец",
                "Получено предложение", "Без условий", "", "Ответить", DateTimeOffset.UtcNow.AddMinutes(-1)), "neg", CancellationToken.None);
        card = await fixture.Workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        Guid negotiationId = card.Negotiations.Single().Id;
        byte[] bytes = Encoding.UTF8.GetBytes("phase-4-document");
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => fixture.Workspace.AddNegotiationAsync(fixture.SecondManager,
            new(taken.CaseId, card.Item.CaseVersion, NegotiationPriceType.BuyerOffer, 1_900_000m, "Звонок", "Продавец",
                "Нет доступа", "", "", "", DateTimeOffset.UtcNow.AddMinutes(-1)), "scope-neg", CancellationToken.None));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => fixture.Workspace.SaveCheckAsync(fixture.SecondManager,
            new(taken.CaseId, null, card.Item.CaseVersion, null, CaseCheckLevel.Quick, "Чужая проверка",
                CaseCheckStatus.Planned, null, null, null, "", false), "scope-check", CancellationToken.None));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => fixture.Workspace.AddAttachmentAsync(fixture.SecondManager,
            new(taken.CaseId, CaseAttachmentOwner.Case, null, CaseAttachmentKind.Document, "Чужой файл", "foreign.txt", "text/plain", bytes, null),
            "scope-file", CancellationToken.None));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => fixture.Workspace.ApplySourceFactAsync(fixture.SecondManager,
            new(taken.CaseId, itemId, card.Item.CaseVersion, CaseFactField.Title), "scope-fact", CancellationToken.None));
        Guid fileId = await fixture.Workspace.AddAttachmentAsync(fixture.Manager,
            new(taken.CaseId, CaseAttachmentOwner.Case, null, CaseAttachmentKind.Document, "Заключение", "conclusion.txt", "text/plain", bytes, null),
            "file", CancellationToken.None);
        Guid linkId = await fixture.Workspace.AddAttachmentAsync(fixture.Manager,
            new(taken.CaseId, CaseAttachmentOwner.Negotiation, negotiationId, CaseAttachmentKind.Link, "Переписка", "", "", null, "https://example.test/dialog"),
            "link", CancellationToken.None);

        AttachmentContent file = await fixture.Workspace.ReadAttachmentAsync(fixture.Manager, fileId, CancellationToken.None);
        CollectionAssert.AreEqual(bytes, file.Content!);
        AttachmentContent link = await fixture.Workspace.ReadAttachmentAsync(fixture.Owner, linkId, CancellationToken.None);
        Assert.AreEqual("https://example.test/dialog", link.ExternalUrl);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => fixture.Workspace.ReadAttachmentAsync(fixture.SecondManager, fileId, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => fixture.Workspace.ReadAttachmentAsync(fixture.ForeignOwner, fileId, CancellationToken.None));
        card = await fixture.Workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        Assert.AreEqual(2, card.Attachments.Count);
        Assert.IsFalse(typeof(AttachmentView).GetProperties().Any(property => property.Name.Contains("StorageKey", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task SourceDiscrepancyRequiresExplicitApplyAndReopenKeepsTheSameCase()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        var pair = await fixture.IngestMarketplacePairAsync();
        Guid avitoId = pair.AvitoId;
        TakeToWorkResult taken = await fixture.Workspace.TakeToWorkAsync(fixture.Manager, new(avitoId), "take", CancellationToken.None);
        await fixture.IngestChangedAvitoAsync(pair.Agent, pair.Administration, 1_700_000m);
        CaseCard card = await fixture.Workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        Assert.AreEqual(2_000_000m, card.Item.Price);
        Assert.IsTrue(card.Discrepancies.Any(item => item.CatalogItemId == avitoId && item.Field == CaseFactField.Price && item.Different));
        await fixture.Workspace.ApplySourceFactAsync(fixture.Manager,
            new(taken.CaseId, avitoId, card.Item.CaseVersion, CaseFactField.Price), "apply", CancellationToken.None);
        card = await fixture.Workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        Assert.AreEqual(1_700_000m, card.Item.Price);
        Assert.AreEqual(1, await fixture.CountAsync(db => db.PropertyCaseFactRevisions.CountAsync(item => item.PropertyCaseId == taken.CaseId)));

        await fixture.Workspace.DecideAsync(fixture.Manager, new(taken.CaseId, card.Item.CaseVersion, card.Item.SourceRevision,
            ProcurementAction.Reject, "Объект временно не подходит", "", null, null), "reject", CancellationToken.None);
        var source = (await fixture.Workspace.ReadItemAsync(fixture.Manager, avitoId, CancellationToken.None)).Item;
        TakeToWorkResult resumed = await fixture.Workspace.ResumeCaseAsync(fixture.Manager, new(avitoId, source.Version), "resume", CancellationToken.None);
        Assert.AreEqual(taken.CaseId, resumed.CaseId);
        Assert.AreEqual(1, await fixture.CountAsync(db => db.PropertyCases.CountAsync()));
        Assert.AreEqual(1, await fixture.CountAsync(db => db.PropertyCaseSourceLinks.CountAsync(item => item.CatalogItemId == avitoId && item.Confirmed)));
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task PropertyCaseDossierBrowserFlowUsesApprovedTabsOnDesktopAndMobile()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        Guid itemId = await fixture.Workspace.CreateManualAsync(fixture.Manager,
            new(CatalogSource.Telegram, "UI PropertyCase Phase 4", "Химки", 19_000_000m, 840m, null, null,
                "50:10:0010203:418", "Карточка Phase 4", "Browser verification"), "browser", CancellationToken.None);
        TakeToWorkResult taken = await fixture.Workspace.TakeToWorkAsync(fixture.Manager, new(itemId), "take", CancellationToken.None);
        await ProcurementUiScenario.RunPhase4Async(fixture.Sandbox, "manager-phase1@test.invalid", taken.CaseId);
    }
}
