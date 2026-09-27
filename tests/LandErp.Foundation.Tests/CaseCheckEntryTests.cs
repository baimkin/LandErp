using System.Text.Json;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.IdentityAccess.Domain;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Foundation.Tests;

[TestClass]
public sealed class CaseCheckEntryTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static string Text(string value) => CaseNoteDocument.FromPlainText(value);
    private static string Image(Guid id) => JsonSerializer.Serialize(new { type = "doc", content = new[] { new { type = "image", attrs = new { attachmentId = id } } } });
    private static SaveCaseCheck Edit(Guid caseId, long caseVersion, CheckView check, string? json) =>
        new(caseId, check.Id, caseVersion, check.Version, check.Level, check.Title, check.Status,
            null, false, check.DueAt, check.Cost, check.Result, check.Blocker, ResultDocumentJson: json);

    [TestMethod]
    public async Task ReadyResultsNeedNoAssignmentAndSectionsAndTemplatesRemainIndependent()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        Guid id = await f.InsertIndependentCaseAsync("Готовые результаты");
        long version = (await f.ReadCaseAsync(id)).Version;
        Guid quick = await f.Workspace.SaveCheckWithIdAsync(f.Manager, new(id, null, version, null, CaseCheckLevel.Quick,
            "Документы сверены", CaseCheckStatus.Passed, null, false, null, null, "", false,
            ResultDocumentJson: Text("Право подтверждено")), "c02-ready", Ct);
        var card = await f.Workspace.ReadCardAsync(f.Manager, id, Ct);
        var check = card.Checks.Single(item => item.Id == quick);
        Assert.IsNull(check.ResponsibleEmployeeId); Assert.IsNull(check.DueAt); Assert.IsNull(check.Cost);
        Assert.AreEqual(CaseCheckStatus.Passed, check.Status); Assert.IsFalse(check.Blocker);
        Assert.AreEqual("Право подтверждено", check.Result);
        Assert.IsNotNull(check.ResultDocumentJson);
        Assert.AreEqual(0, card.RichNotes.Count);
        await f.Workspace.SaveRichNoteAsync(f.Manager, new(id, CaseNoteSection.QuickChecks, 0, Text("Общие выводы секции")), "c02-section", Ct);
        Assert.AreEqual(check, (await f.Workspace.ReadCardAsync(f.Manager, id, Ct)).Checks.Single());

        var deep = card.CheckTemplates.First(item => item.Active && item.Level == CaseCheckLevel.Deep);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => f.Workspace.SaveCheckAsync(f.Manager,
            new(id, null, version, null, CaseCheckLevel.Quick, deep.Title, CaseCheckStatus.Planned,
                null, false, null, null, "", false, deep.Id), "c02-template-depth", Ct));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => f.Workspace.SaveCheckAsync(f.Manager,
            new(id, null, version, null, CaseCheckLevel.Deep, deep.Title, CaseCheckStatus.Passed,
                null, false, null, null, "", false, deep.Id, Text("Заключение готово")), "c02-deep-early", Ct));
        await SetStage(f, id, "approved"); version = (await f.ReadCaseAsync(id)).Version;
        Guid deepId = await f.Workspace.SaveCheckWithIdAsync(f.Manager, new(id, null, version, null, CaseCheckLevel.Deep,
            deep.Title, CaseCheckStatus.Issue, null, false, null, null, "", false, deep.Id, Text("Нужно уточнить границы")), "c02-deep", Ct);
        check = (await f.Workspace.ReadCardAsync(f.Manager, id, Ct)).Checks.Single(item => item.Id == deepId);
        Assert.AreEqual(CaseCheckLevel.Deep, check.Level); Assert.AreEqual(deep.Id, check.TemplateItemId);
        Assert.AreEqual(deep.Description, check.DescriptionSnapshot); Assert.IsFalse(check.Blocker);
        Assert.IsNull(check.ResponsibleEmployeeId); Assert.IsNull(check.DueAt);
        await using var db = f.Sandbox.Context();
        Assert.IsFalse(db.Database.HasPendingModelChanges());
        string comment = await db.Database.SqlQueryRaw<string>("SELECT col_description('procurement.case_checks'::regclass, attnum) AS \"Value\" FROM pg_attribute WHERE attrelid='procurement.case_checks'::regclass AND attname='result_document_json'").SingleAsync();
        StringAssert.Contains(comment, "Null означает прежний plain text");
    }

    [TestMethod]
    public async Task LegacyStatesBlockerAssignmentAndPastDueSurviveRichEditWithAuditAndConcurrency()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        Guid id = await f.InsertIndependentCaseAsync("Прежние проверки");
        long version = (await f.ReadCaseAsync(id)).Version;
        foreach (var state in new[] { CaseCheckStatus.InProgress, CaseCheckStatus.Blocked })
        {
            Guid checkId = await f.Workspace.SaveCheckWithIdAsync(f.Head, new(id, null, version, null,
                CaseCheckLevel.Quick, "Старая проверка " + state, state, f.ManagerEmployeeId, true,
                DateTimeOffset.UtcNow.AddDays(2), 123.456m, "<b>Старый текст</b>\nВторая строка", true), "c02-legacy", Ct);
            await using (var db = f.Sandbox.Context())
            {
                var row = await db.CaseChecks.SingleAsync(item => item.Id == checkId);
                row.DueAt = DateTimeOffset.UtcNow.AddDays(-2); await db.SaveChangesAsync();
            }
            var check = (await f.Workspace.ReadCardAsync(f.Manager, id, Ct)).Checks.Single(item => item.Id == checkId);
            Assert.IsNull(check.ResultDocumentJson);
            var command = Edit(id, version, check, Text(new string('я', 5000)));
            await f.Workspace.SaveCheckAsync(f.Manager, command, "c02-rich-edit", Ct);
            var updated = (await f.Workspace.ReadCardAsync(f.Manager, id, Ct)).Checks.Single(item => item.Id == checkId);
            Assert.AreEqual(state, updated.Status); Assert.IsTrue(updated.Blocker);
            Assert.AreEqual(check.ResponsibleEmployeeId, updated.ResponsibleEmployeeId);
            Assert.AreEqual(check.DueAt, updated.DueAt); Assert.AreEqual(check.Cost, updated.Cost);
            Assert.AreEqual(123.46m, updated.Cost); Assert.AreEqual(check.Version + 1, updated.Version);
            Assert.AreEqual(4000, updated.Result.Length);
            Assert.AreEqual(5000, CaseNoteDocument.PlainText(updated.ResultDocumentJson!).Length);
            await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(() => f.Workspace.SaveCheckAsync(f.Manager, command, "c02-stale", Ct));
            await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => f.Workspace.SaveCheckAsync(f.Manager,
                Edit(id, version, updated, Text("Текст без снятия блокера")) with { Blocker = false }, "c02-blocker", Ct));
            await f.Workspace.SaveCheckAsync(f.Manager, Edit(id, version, updated, null), "c02-old-client-same", Ct);
            updated = (await f.Workspace.ReadCardAsync(f.Manager, id, Ct)).Checks.Single(item => item.Id == checkId);
            Assert.IsNotNull(updated.ResultDocumentJson);
            await f.Workspace.SaveCheckAsync(f.Manager, Edit(id, version, updated, null) with { Result = "<script>новый обычный текст</script>" }, "c02-old-client-edit", Ct);
            updated = (await f.Workspace.ReadCardAsync(f.Manager, id, Ct)).Checks.Single(item => item.Id == checkId);
            Assert.IsNull(updated.ResultDocumentJson);
            StringAssert.Contains(updated.Result, "<script>");
            await using var auditDb = f.Sandbox.Context();
            var audit = await auditDb.AuditEvents.Where(item => item.ObjectId == id && item.CorrelationId == "c02-rich-edit").OrderByDescending(item => item.RecordedAt).FirstAsync();
            using var payload = JsonDocument.Parse(audit.Changes);
            Assert.AreEqual(check.Result, payload.RootElement.GetProperty("Before").GetProperty("Result").GetString());
            Assert.AreEqual(5000, CaseNoteDocument.PlainText(payload.RootElement.GetProperty("CurrentDocument").GetString()!).Length);
        }
    }

    [TestMethod]
    public async Task RichResultsKeepCheckImageOwnershipPermissionsAndClosedStageGuards()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(true, true);
        Guid id = await f.InsertIndependentCaseAsync("Защита результата");
        long version = (await f.ReadCaseAsync(id)).Version;
        Guid checkId = await f.Workspace.SaveCheckWithIdAsync(f.Manager, new(id, null, version, null, CaseCheckLevel.Quick,
            "Первая проверка", CaseCheckStatus.Planned, null, false, null, null, "", false), "c02-first", Ct);
        Guid otherCheckId = await f.Workspace.SaveCheckWithIdAsync(f.Manager, new(id, null, version, null, CaseCheckLevel.Quick,
            "Другая проверка", CaseCheckStatus.Planned, null, false, null, null, "", false), "c02-second", Ct);
        byte[] bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl6R04AAAAASUVORK5CYII=");
        Guid image = await f.Workspace.AddAttachmentAsync(f.Manager, new(id, CaseAttachmentOwner.Check, checkId,
            CaseAttachmentKind.Photo, "Материал проверки", "", "image.png", "image/png", bytes, null), "c02-image", Ct);
        var card = await f.Workspace.ReadCardAsync(f.Manager, id, Ct);
        var check = card.Checks.Single(item => item.Id == checkId);
        var other = card.Checks.Single(item => item.Id == otherCheckId);
        await f.Workspace.SaveCheckAsync(f.Manager, Edit(id, version, check, Image(image)) with { Status = CaseCheckStatus.Passed }, "c02-image-result", Ct);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => f.Workspace.SaveCheckAsync(f.Manager, Edit(id, version, other, Image(image)), "c02-other-check", Ct));
        // The shared validator must not widen C-01 section notes to check-owned attachments.
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => f.Workspace.SaveRichNoteAsync(f.Manager,
            new(id, CaseNoteSection.QuickChecks, 0, Image(image)), "c02-section-image", Ct));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => f.Workspace.SaveCheckAsync(f.SecondManager,
            Edit(id, version, other, Text("чужой отдел")), "c02-department", Ct));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => f.Workspace.SaveCheckAsync(f.ForeignOwner,
            Edit(id, version, other, Text("чужая организация")), "c02-organization", Ct));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => f.Workspace.SaveCheckAsync(f.Manager,
            Edit(id, version, other, """{"type":"doc","content":[{"type":"script"}]}"""), "c02-unsafe", Ct));
        await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(() => f.Workspace.SaveCheckAsync(f.Manager,
            Edit(id, version - 1, other, Text("устаревшая карточка")), "c02-case-version", Ct));
        foreach (string stage in new[] { "acquired", "rejected", "monitor" })
        {
            await SetStage(f, id, stage);
            long closedVersion = (await f.ReadCaseAsync(id)).Version;
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => f.Workspace.SaveCheckAsync(f.Manager,
                Edit(id, closedVersion, other, Text("закрытый объект")), "c02-closed", Ct));
        }
        await SetStage(f, id, "analysis");
        await f.SetExplicitAccessAsync(f.ManagerEmployeeId,
            ProcurementTestsHelper.ProcurementManagerAccess(AccessScope.Department) with { ProcurementAccess = ProcurementAccessLevel.Read });
        Assert.AreEqual(2, (await f.Workspace.ReadCardAsync(f.Manager, id, Ct)).Checks.Count);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => f.Workspace.SaveCheckAsync(f.Manager,
            Edit(id, version, other, Text("только чтение")), "c02-readonly", Ct));
    }

    [TestMethod]
    public void LegacyPlainTextIsNeverHtmlAndKeepsLineBreaksWithoutNodeExplosion()
    {
        string original = "<img src=x onerror=alert(1)>\r\n<script>текст</script>";
        var result = CaseNoteDocument.Validate(Text(original));
        StringAssert.Contains(result.Html, "&lt;img");
        Assert.IsFalse(result.Html.Contains("<script>", StringComparison.Ordinal));
        Assert.AreEqual(original, CaseNoteDocument.PlainText(result.Json));
        string manyLines = string.Concat(Enumerable.Repeat("x\n", 1999)).TrimEnd();
        Assert.AreEqual(manyLines, CaseNoteDocument.PlainText(CaseNoteDocument.Validate(Text(manyLines)).Json));
    }

    private static async Task SetStage(ProcurementTests.Phase1Fixture f, Guid id, string stage)
    {
        await using var db = f.Sandbox.Context();
        (await db.PropertyCases.SingleAsync(item => item.Id == id)).StageId = stage;
        await db.SaveChangesAsync();
    }
}
