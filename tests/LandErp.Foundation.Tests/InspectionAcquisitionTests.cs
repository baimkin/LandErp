using System.Text;
using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Application.Foundation.Files;
using LandErp.Infrastructure.Modules.Procurement;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
[TestCategory("PostgreSQL")]
public sealed class InspectionAcquisitionTests
{
    private static readonly string[] ExpectedCheckTemplates = ["Собственник", "Обременения", "Категория / ВРИ", "Подъезд", "ПЗЗ / генплан", "Юридическая проверка"];

    [TestMethod]
    public async Task DocumentChecklistTracksRequestAttachmentVerificationAndHistory()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        Guid itemId = await fixture.CreateUnlinkedManualAsync();
        TakeToWorkResult taken = await fixture.Workspace.TakeToWorkAsync(fixture.Manager, new(itemId), "take", CancellationToken.None);
        CaseCard card = await fixture.Workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        Assert.AreEqual(5, card.DocumentRequirements.Count);
        Assert.IsTrue(card.DocumentRequirements.All(item => item.Status == CaseDocumentStatus.Missing));
        DocumentRequirementView egrn = card.DocumentRequirements.Single(item => item.Code == "egrn");
        DateTimeOffset due = new(DateTime.UtcNow.Date.AddDays(2), TimeSpan.Zero);
        await fixture.Workspace.SaveDocumentRequirementAsync(fixture.Manager,
            new(taken.CaseId, egrn.Id, card.Item.CaseVersion, egrn.Version, CaseDocumentStatus.Requested, due, "Запрошена свежая выписка"),
            "request-document", CancellationToken.None);
        card = await fixture.Workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        egrn = card.DocumentRequirements.Single(item => item.Id == egrn.Id);
        Assert.AreEqual(CaseDocumentStatus.Requested, egrn.Status);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.Workspace.SaveDocumentRequirementAsync(fixture.Manager,
            new(taken.CaseId, egrn.Id, card.Item.CaseVersion, egrn.Version, CaseDocumentStatus.Verified, due, "Без файла"),
            "verify-without-file", CancellationToken.None));

        byte[] content = Encoding.UTF8.GetBytes("synthetic-egrn");
        Guid attachmentId = await fixture.Workspace.AddAttachmentAsync(fixture.Manager,
            new(taken.CaseId, CaseAttachmentOwner.Case, null, CaseAttachmentKind.Document, "Выписка ЕГРН",
                "Получена от Росреестра", "egrn.pdf", "application/pdf", content, null, egrn.Id),
            "attach-document", CancellationToken.None);
        card = await fixture.Workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        egrn = card.DocumentRequirements.Single(item => item.Id == egrn.Id);
        Assert.AreEqual(CaseDocumentStatus.Received, egrn.Status);
        CollectionAssert.Contains(egrn.AttachmentIds.ToArray(), attachmentId);
        await fixture.Workspace.SaveDocumentRequirementAsync(fixture.Manager,
            new(taken.CaseId, egrn.Id, card.Item.CaseVersion, egrn.Version, CaseDocumentStatus.Verified, null, "Сведения сверены"),
            "verify-document", CancellationToken.None);
        card = await fixture.Workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        Assert.AreEqual(CaseDocumentStatus.Verified, card.DocumentRequirements.Single(item => item.Id == egrn.Id).Status);
        Assert.IsTrue(card.Timeline.Any(item => item.Kind == "Document" && item.Title.Contains("проверен")));
        Assert.AreEqual(2, await fixture.CountAsync(db => db.AuditEvents.CountAsync(item => item.ObjectId == taken.CaseId
            && item.Action == "CaseDocumentRequirementChanged")));
    }
    [TestMethod]
    public async Task Phase4CorrectionsPreserveResponsibleTemplateSnapshotAndCaseCadastralNumber()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        Guid itemId = await fixture.Workspace.CreateManualAsync(fixture.Manager,
            new(CatalogSource.Manual, "Коррекции Phase 4", "Химки", 4_000_000m, 900m, null, null, null, null, "Targeted"),
            "phase4-corrections", CancellationToken.None);
        TakeToWorkResult taken = await fixture.Workspace.TakeToWorkAsync(fixture.Manager, new(itemId), "take", CancellationToken.None);
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            var propertyCase = await db.PropertyCases.SingleAsync(item => item.Id == taken.CaseId);
            propertyCase.CadastralNumber = "50:10:0000000:777";
            await db.SaveChangesAsync();
        }
        CaseCard card = await fixture.Workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        Assert.AreEqual("50:10:0000000:777", card.CadastralNumber);
        CollectionAssert.IsSubsetOf(ExpectedCheckTemplates, card.CheckTemplates.Select(item => item.Title).ToArray());
        await fixture.Workspace.SaveCheckTemplateAsync(fixture.Manager, new(null, null, "Экология", CaseCheckLevel.Deep,
            "Проверить экологические ограничения.", 70, true), "create-template", CancellationToken.None);
        card = await fixture.Workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        Assert.IsTrue(card.CheckTemplates.Any(item => item.Title == "Экология" && item.Level == CaseCheckLevel.Deep));
        CheckTemplateView template = card.CheckTemplates.Single(item => item.Title == "Собственник");
        await fixture.Workspace.SaveCheckAsync(fixture.Manager, new(taken.CaseId, null, card.Item.CaseVersion, null,
            template.Level, template.Title, CaseCheckStatus.InProgress, fixture.ManagerEmployeeId, true, null, null,
            "Запрошена выписка", false, template.Id), "from-template", CancellationToken.None);
        card = await fixture.Workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        CheckView check = card.Checks.Single(); string snapshot = check.DescriptionSnapshot;
        await fixture.Workspace.SaveCheckTemplateAsync(fixture.Manager, new(template.Id, template.Version, "Собственник / право",
            template.Level, "Новое описание", template.SortOrder, true), "edit-template", CancellationToken.None);
        await fixture.Workspace.SaveCheckAsync(fixture.Manager, new(taken.CaseId, check.Id, card.Item.CaseVersion, check.Version,
            check.Level, check.Title, CaseCheckStatus.Passed, null, false, null, null, "Собственник подтверждён", false),
            "edit-without-responsible", CancellationToken.None);
        card = await fixture.Workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        check = card.Checks.Single();
        Assert.AreEqual(fixture.ManagerEmployeeId, check.ResponsibleEmployeeId, "Ordinary edit must not erase an existing optional responsible employee.");
        Assert.AreEqual("Собственник", check.Title); Assert.AreEqual(snapshot, check.DescriptionSnapshot, "Template edit must not rewrite a CaseCheck snapshot.");

        await fixture.Workspace.SaveCheckAsync(fixture.Manager, new(taken.CaseId, null, card.Item.CaseVersion, null,
            CaseCheckLevel.Quick, "Ad-hoc проверка", CaseCheckStatus.Planned, null, false, null, null, "", false), "adhoc", CancellationToken.None);
        card = await fixture.Workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        CheckView first = card.Checks.Single(item => item.Title == "Собственник"); CheckView second = card.Checks.Single(item => item.Title == "Ad-hoc проверка");
        await Task.WhenAll(
            fixture.Workspace.SaveCheckAsync(fixture.Manager, new(taken.CaseId, first.Id, card.Item.CaseVersion, first.Version, first.Level,
                first.Title, CaseCheckStatus.Passed, null, false, null, null, "Параллельная правка 1", false), "parallel-1", CancellationToken.None),
            fixture.Workspace.SaveCheckAsync(fixture.Manager, new(taken.CaseId, second.Id, card.Item.CaseVersion, second.Version, second.Level,
                second.Title, CaseCheckStatus.Issue, null, false, null, null, "Параллельная правка 2", false), "parallel-2", CancellationToken.None));
    }

    [TestMethod]
    public async Task InspectionSnapshotReconnectMediaAndAcquisitionAreSafeAndTerminal()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(true, true);
        Guid itemId = await fixture.CreateUnlinkedManualAsync();
        TakeToWorkResult taken = await fixture.Workspace.TakeToWorkAsync(fixture.Manager, new(itemId), "take", CancellationToken.None);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => fixture.Workspace.SaveInspectionAsync(fixture.ForeignOwner,
            new(taken.CaseId, null, null, "", "", [], false), "foreign", CancellationToken.None));
        Guid inspectionId = await fixture.Workspace.SaveInspectionAsync(fixture.Manager,
            new(taken.CaseId, null, null, "", "", [], false), "start", CancellationToken.None);
        CaseCard card = await fixture.Workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        Assert.AreEqual(20, card.Inspection!.Items.Count); string firstTitle = card.Inspection.Items[0].Title;
        InspectionTemplateView template = await FirstInspectionTemplateAsync(fixture, card.Inspection.Items[0].TemplateItemId);
        await fixture.Workspace.SaveInspectionTemplateAsync(fixture.Manager, new(template.Id, template.Version, template.Key,
            template.Title + " изменено", template.SortOrder, template.AnswerType, template.Options, template.Unit,
            template.NormalAnswer, template.AllowAttachments, template.Required, template.Active), "template-change", CancellationToken.None);
        card = await fixture.Workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        Assert.AreEqual(firstTitle, card.Inspection!.Items[0].Title, "Started inspection must keep the template snapshot.");

        InspectionView draft = card.Inspection;
        InspectionAnswer[] answers = draft.Items.Select(item => new InspectionAnswer(item.Id, item.Version,
            item == draft.Items[0] ? InspectionItemStatus.Answered : InspectionItemStatus.NotChecked,
            item == draft.Items[0] ? (item.AnswerType == InspectionAnswerType.Boolean ? "true" : "Проверено") : "", "")).ToArray();
        await fixture.Workspace.SaveInspectionAsync(fixture.Manager, new(taken.CaseId, inspectionId, draft.Version,
            "Черновик после восстановления связи", "Нужны дополнительные проверки", answers, false), "reconnect", CancellationToken.None);
        await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(() => fixture.Workspace.SaveInspectionAsync(fixture.Manager,
            new(taken.CaseId, inspectionId, draft.Version, "Устаревший локальный черновик", "", answers, false), "stale", CancellationToken.None));

        card = await fixture.Workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        byte[] photo = Encoding.UTF8.GetBytes("inspection-photo");
        ProcurementWorkspace failingUpload = new(fixture.Factory, fixture.Access, TimeProvider.System, new FailingFileStorage());
        await Assert.ThrowsExactlyAsync<IOException>(() => failingUpload.AddAttachmentAsync(fixture.Manager,
            new(taken.CaseId, CaseAttachmentOwner.Inspection, inspectionId, CaseAttachmentKind.Photo,
                "Фото с временной ошибкой", "Ссылка должна сохраниться", "retry.jpg", "image/jpeg", photo, null),
            "failed-media", CancellationToken.None));
        card = await fixture.Workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        AttachmentView failed = card.Attachments.Single(item => item.Label == "Фото с временной ошибкой");
        Assert.AreEqual(StoredFileStatus.UploadFailed, failed.Status);
        await fixture.Workspace.RetryAttachmentAsync(fixture.Manager,
            new(taken.CaseId, failed.Id, "retry.jpg", "image/jpeg", photo), "retry-media", CancellationToken.None);
        card = await fixture.Workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        Assert.AreEqual(StoredFileStatus.Available, card.Attachments.Single(item => item.Id == failed.Id).Status);

        Guid general = await fixture.Workspace.AddAttachmentAsync(fixture.Manager, new(taken.CaseId, CaseAttachmentOwner.Inspection,
            inspectionId, CaseAttachmentKind.Photo, "Общий вид", "Фото осмотра в целом", "general.jpg", "image/jpeg", photo, null), "general-media", CancellationToken.None);
        Guid point = await fixture.Workspace.AddAttachmentAsync(fixture.Manager, new(taken.CaseId, CaseAttachmentOwner.InspectionItem,
            card.Inspection!.Items[0].Id, CaseAttachmentKind.Audio, "Комментарий по дороге", "Аудиозаметка пункта", "road.mp3", "audio/mpeg", photo, null), "item-media", CancellationToken.None);
        card = await fixture.Workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        Assert.AreEqual("Осмотр участка", card.Attachments.Single(item => item.Id == general).OwnerLabel);
        StringAssert.StartsWith(card.Attachments.Single(item => item.Id == point).OwnerLabel, "Пункт осмотра · ");

        InspectionView current = card.Inspection!;
        await fixture.Workspace.SaveInspectionAsync(fixture.Manager, new(taken.CaseId, current.Id, current.Version,
            "Критичных проблем не выявлено", "Осмотр пройден, критичных проблем нет",
            current.Items.Select(item => new InspectionAnswer(item.Id, item.Version, item.Status, item.Answer, item.Note)).ToArray(), true),
            "complete", CancellationToken.None);
        card = await fixture.Workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        DateOnly acquiredDate = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        Assert.IsFalse(card.CanConfirmPurchase, "Обычный менеджер не должен подтверждать покупку.");
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => fixture.Workspace.MarkAcquiredAsync(fixture.Manager,
            new(taken.CaseId, card.Item.CaseVersion, 3_750_000m, acquiredDate, "Право зарегистрировано"), "manager-acquire", CancellationToken.None));
        CaseCard headCard = await fixture.Workspace.ReadCardAsync(fixture.Head, taken.CaseId, CancellationToken.None);
        Assert.IsTrue(headCard.CanConfirmPurchase, "Руководитель закупок должен подтверждать покупку.");
        Assert.IsTrue((await fixture.Workspace.ReadCardAsync(fixture.Owner, taken.CaseId, CancellationToken.None)).CanConfirmPurchase,
            "Владелец должен подтверждать покупку.");
        await fixture.Workspace.MarkAcquiredAsync(fixture.Head, new(taken.CaseId, headCard.Item.CaseVersion, 3_750_000m, acquiredDate,
            "Право зарегистрировано"), "acquire", CancellationToken.None);
        card = await fixture.Workspace.ReadCardAsync(fixture.Head, taken.CaseId, CancellationToken.None);
        Assert.AreEqual("acquired", card.Item.Stage); Assert.AreEqual(3_750_000m, card.AcquisitionPrice); Assert.AreEqual(acquiredDate, card.AcquisitionDate);
        Assert.AreEqual("Право зарегистрировано", card.AcquisitionComment);
        Assert.AreEqual(InspectionStatus.Completed, card.Inspection!.Status); Assert.IsTrue(card.Timeline.Any(item => item.Kind == "Acquisition"));
        await fixture.Workspace.MarkAcquiredAsync(fixture.Head, new(taken.CaseId, card.Item.CaseVersion, 3_750_000m, acquiredDate,
            "Право зарегистрировано"), "idempotent", CancellationToken.None);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.Workspace.MarkAcquiredAsync(fixture.Head,
            new(taken.CaseId, card.Item.CaseVersion, 3_800_000m, acquiredDate, "Другая цена"), "duplicate", CancellationToken.None));
        await fixture.Workspace.CorrectAcquisitionAsync(fixture.Head,
            new(taken.CaseId, card.Item.CaseVersion, 3_800_000m, acquiredDate, "Исправлено по договору", "Исправлена опечатка в цене"),
            "correct", CancellationToken.None);
        card = await fixture.Workspace.ReadCardAsync(fixture.Head, taken.CaseId, CancellationToken.None);
        Assert.AreEqual(3_800_000m, card.AcquisitionPrice);
        Assert.AreEqual("Исправлено по договору", card.AcquisitionComment);
        Assert.IsTrue(card.Timeline.Any(item => item.Kind == "AcquisitionCorrection" && item.Body.Contains("Исправлена опечатка")));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => fixture.Workspace.CorrectAcquisitionAsync(fixture.Manager,
            new(taken.CaseId, card.Item.CaseVersion, 3_900_000m, acquiredDate, "", "Попытка менеджера"), "manager-correct", CancellationToken.None));
        Assert.AreEqual(1, await fixture.CountAsync(db => db.BusinessTimeline.CountAsync(item => item.ObjectId == taken.CaseId && item.Kind == "Acquisition")));
        Assert.AreEqual(1, await fixture.CountAsync(db => db.BusinessTimeline.CountAsync(item => item.ObjectId == taken.CaseId && item.Kind == "AcquisitionCorrection")));
        Assert.AreEqual(1, await fixture.CountAsync(db => db.AuditEvents.CountAsync(item => item.ObjectId == taken.CaseId && item.Action == "PropertyCaseAcquired")));
        Assert.AreEqual(1, await fixture.CountAsync(db => db.AuditEvents.CountAsync(item => item.ObjectId == taken.CaseId && item.Action == "PropertyCaseAcquisitionCorrected")));
        Assert.IsTrue(await fixture.CountAsync(db => db.WorkTasks.Where(item => item.ObjectId == taken.CaseId).Select(item => item.Completed).CountAsync(value => value)) == 1);
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task FullProcurementCycleInspectionOfflineDraftAndAcquiredWorkInBrowser()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        Guid itemId = await fixture.Workspace.CreateManualAsync(fixture.Manager,
            new(CatalogSource.Manual, "UI PropertyCase Phase 5", "Химки", 4_000_000m, 900m, null, null,
                "50:10:0000000:505", "Полный цикл Phase 5", "Browser verification"), "phase5-browser", CancellationToken.None);
        TakeToWorkResult taken = await fixture.Workspace.TakeToWorkAsync(fixture.Manager, new(itemId), "take", CancellationToken.None);
        CaseCard card = await fixture.Workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        await fixture.Workspace.AddNegotiationAsync(fixture.Manager, new(taken.CaseId, card.Item.CaseVersion, null, null, null,
            "Телефон", "Собственник", "Договорились об осмотре", "", "", "Провести осмотр", null, DateTimeOffset.UtcNow),
            "negotiation", CancellationToken.None);
        card = await fixture.Workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        await fixture.Workspace.SaveCheckAsync(fixture.Manager, new(taken.CaseId, null, card.Item.CaseVersion, null,
            CaseCheckLevel.Quick, "Собственник", CaseCheckStatus.Passed, null, false, null, null, "Подтверждён", false),
            "check", CancellationToken.None);

        await ProcurementUiScenario.RunPhase5Async(fixture.Sandbox, "head-phase1@test.invalid", taken.CaseId);
        card = await fixture.Workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        Assert.AreEqual("acquired", card.Item.Stage);
        Assert.AreEqual(InspectionStatus.Completed, card.Inspection!.Status);
        Assert.AreEqual("Локальный вывод после краткого разрыва связи", card.Inspection.OverallConclusion);
    }

    private static async Task<InspectionTemplateView> FirstInspectionTemplateAsync(ProcurementTests.Phase1Fixture fixture, Guid id)
    {
        await using var db = await fixture.Factory.CreateDbContextAsync();
        InspectionTemplateItem item = await db.InspectionTemplateItems.AsNoTracking().SingleAsync(value => value.Id == id);
        return new(item.Id, item.Key, item.Title, item.SortOrder, item.AnswerType,
            System.Text.Json.JsonSerializer.Deserialize<string[]>(item.OptionsJson) ?? [], item.Unit, item.NormalAnswer,
            item.AllowAttachments, item.Required, item.Active, item.Version);
    }

    private sealed class FailingFileStorage : IFileStorage
    {
        public Task<FileWriteResult> WriteAsync(Guid stableFileId, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
            => Task.FromException<FileWriteResult>(new IOException("Synthetic upload failure."));
        public Task<byte[]> ReadAsync(string storageKey, CancellationToken cancellationToken)
            => Task.FromException<byte[]>(new IOException("Synthetic storage is unavailable."));
    }
}
