using System.Text.Json;
using LandErp.Application.Foundation.Files;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.IdentityAccess.Domain;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Infrastructure.Modules.Organization;
using LandErp.Infrastructure.Modules.Procurement;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Foundation.Tests;

[TestClass]
public sealed class CaseRichNoteTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static string Text(string text) => JsonSerializer.Serialize(new { type = "doc", content = new[] {
        new { type = "paragraph", content = new[] { new { type = "text", text } } } } });
    private static string Image(Guid id) => JsonSerializer.Serialize(new { type = "doc", content = new[] { new { type = "image", attrs = new { attachmentId = id } } } });

    [TestMethod]
    public async Task SectionsSaveIndependentlyWithVersionAuditAndRuntimeSchema()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        Guid id = await f.InsertIndependentCaseAsync("Заметки C-01");
        long caseVersion = (await f.ReadCaseAsync(id)).Version;
        foreach (var section in Enum.GetValues<CaseNoteSection>())
        {
            var saved = await f.Workspace.SaveRichNoteAsync(f.Manager, new(id, section, 0, Text(section.ToString())), "c01-insert", Ct);
            Assert.AreEqual(1L, saved.Version);
            Assert.AreEqual("Manager", saved.UpdatedBy);
            Assert.AreEqual(TimeSpan.Zero, saved.UpdatedAt.Offset);
        }
        var changed = await f.Workspace.SaveRichNoteAsync(f.Manager, new(id, CaseNoteSection.Working, 1, Text(new string('я', 12000))), "c01-change", Ct);
        Assert.AreEqual(2L, changed.Version);
        await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(() => f.Workspace.SaveRichNoteAsync(f.Manager,
            new(id, CaseNoteSection.Working, 1, Text("устаревшая запись")), "c01-stale", Ct));
        var card = await f.Workspace.ReadCardAsync(f.Manager, id, Ct);
        Assert.AreEqual(3, card.RichNotes.Count);
        Assert.AreEqual(1L, card.RichNotes.Single(item => item.Section == CaseNoteSection.QuickChecks).Version);
        Assert.AreEqual(1L, card.RichNotes.Single(item => item.Section == CaseNoteSection.DeepChecks).Version);
        Assert.AreEqual(caseVersion, card.Item.CaseVersion);
        Assert.AreEqual(4, card.Timeline.Count(item => item.Title == "Обновлён текст секции"));
        await using var db = f.Sandbox.Context();
        var audit = await db.AuditEvents.Where(item => item.ObjectId == id && item.Action == "CaseRichNoteChanged").ToArrayAsync();
        Assert.AreEqual(4, audit.Length);
        var change = audit.Single(item => item.CorrelationId == "c01-change");
        Assert.AreEqual(f.Manager.UserId, change.ActorId);
        using var detail = JsonDocument.Parse(change.Changes);
        StringAssert.Contains(detail.RootElement.GetProperty("Previous").GetString()!, "Working");
        Assert.AreEqual(changed.DocumentJson, detail.RootElement.GetProperty("Current").GetString());
        var auditPage = await new AuditReadService(f.Factory, f.Access).ReadAsync(f.Owner, new(), Ct);
        var displayed = auditPage.Items.Single(item => item.Id == change.Id);
        Assert.AreEqual("Изменён текст секции объекта", displayed.Title);
        Assert.AreEqual("Рабочие заметки и расчёты", displayed.Changes.Single().Field);
        Assert.AreEqual("Working", displayed.Changes.Single().Before);
        Assert.IsFalse(displayed.Changes.Single().After.Contains("\"type\"", StringComparison.Ordinal));
        Assert.IsFalse(db.Database.HasPendingModelChanges());
        string comment = await db.Database.SqlQueryRaw<string>("SELECT col_description('procurement.case_rich_notes'::regclass, attnum) AS \"Value\" FROM pg_attribute WHERE attrelid='procurement.case_rich_notes'::regclass AND attname='document_json'").SingleAsync();
        StringAssert.Contains(comment, "Очищенный сервером JSON");
    }

    [TestMethod]
    public async Task ConcurrentFirstAndSubsequentSavesHaveOneWinner()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        Guid id = await f.InsertIndependentCaseAsync("Конкурентные заметки");
        async Task<bool> Save(long version, string value)
        {
            try { await f.Workspace.SaveRichNoteAsync(f.Manager, new(id, CaseNoteSection.Working, version, Text(value)), "c01-race", Ct); return true; }
            catch (DbUpdateConcurrencyException) { return false; }
        }
        foreach (long version in new long[] { 0, 1 })
        {
            bool[] results = await Task.WhenAll(Save(version, "A"), Save(version, "B"));
            Assert.AreEqual(1, results.Count(item => item));
        }
        var notes = (await f.Workspace.ReadCardAsync(f.Manager, id, Ct)).RichNotes;
        Assert.AreEqual(1, notes.Count);
        Assert.AreEqual(2L, notes[0].Version);
    }

    [TestMethod]
    public async Task NotesReuseDossierVisibilityAndProtectedAttachments()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(true, true);
        Guid id = await f.InsertIndependentCaseAsync("Файлы заметок");
        Guid other = await f.InsertIndependentCaseAsync("Другой объект");
        byte[] bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl6R04AAAAASUVORK5CYII=");
        Guid image = await f.Workspace.AddAttachmentAsync(f.Manager, new(id, CaseAttachmentOwner.Case, null,
            CaseAttachmentKind.Photo, "Картинка", "", "image.png", "image/png", bytes, null), "c01-upload", Ct);
        var saved = await f.Workspace.SaveRichNoteAsync(f.Manager, new(id, CaseNoteSection.Working, 0, Image(image)), "c01-image", Ct);
        StringAssert.Contains(saved.SafeHtml, $"/api/procurement/attachments/{image}/image");
        Assert.IsFalse(saved.DocumentJson.Contains("base64", StringComparison.Ordinal));
        CollectionAssert.AreEqual(bytes, (await f.Workspace.ReadAttachmentAsync(f.Manager, image, Ct)).Content!);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => f.Workspace.SaveRichNoteAsync(f.Manager,
            new(other, CaseNoteSection.Working, 0, Image(image)), "c01-wrong-case", Ct));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => f.Workspace.SaveRichNoteAsync(f.ForeignOwner,
            new(id, CaseNoteSection.Working, 1, Text("foreign")), "c01-foreign", Ct));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => f.Workspace.ReadCardAsync(f.ForeignOwner, id, Ct));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => f.Workspace.ReadAttachmentAsync(f.ForeignOwner, image, Ct));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => f.Workspace.SaveRichNoteAsync(f.SecondManager,
            new(id, CaseNoteSection.Working, 1, Text("department")), "c01-department", Ct));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => f.Workspace.ReadCardAsync(f.SecondManager, id, Ct));

        await using (var db = f.Sandbox.Context())
        {
            var link = await db.CaseAttachments.SingleAsync(item => item.Id == image);
            var file = await db.StoredFiles.SingleAsync(item => item.Id == link.StoredFileId);
            foreach (var status in new[] { StoredFileStatus.PendingUpload, StoredFileStatus.UploadFailed, StoredFileStatus.Quarantined, StoredFileStatus.Deleted })
            {
                file.Status = status; await db.SaveChangesAsync();
                await Assert.ThrowsExactlyAsync<ArgumentException>(() => f.Workspace.SaveRichNoteAsync(f.Manager,
                    new(id, CaseNoteSection.Working, 1, Image(image)), "c01-unavailable", Ct));
            }
            file.Status = StoredFileStatus.Available; file.ContentType = "image/svg+xml"; await db.SaveChangesAsync();
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => f.Workspace.SaveRichNoteAsync(f.Manager,
                new(id, CaseNoteSection.Working, 1, Image(image)), "c01-svg", Ct));
            file.ContentType = "image/png";
            file.OrganizationId = await db.Organizations.Where(item => item.Id != f.OrganizationId).Select(item => item.Id).SingleAsync();
            await db.SaveChangesAsync();
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => f.Workspace.SaveRichNoteAsync(f.Manager,
                new(id, CaseNoteSection.Working, 1, Image(image)), "c01-file-org", Ct));
            file.OrganizationId = f.OrganizationId; await db.SaveChangesAsync();
        }
        // A failed provider write leaves the existing durable link; recovery must reuse its ID.
        var recoverable = new ProcurementWorkspace(f.Factory, TimeProvider.System, new FailFirstWriteStorage(f.FileStorage));
        await Assert.ThrowsExactlyAsync<IOException>(() => recoverable.AddAttachmentAsync(f.Manager, new(id,
            CaseAttachmentOwner.Case, null, CaseAttachmentKind.Photo, "Повтор", "", "retry.png", "image/png", bytes, null), "c01-failed-upload", Ct));
        var failed = (await f.Workspace.ReadCardAsync(f.Manager, id, Ct)).Attachments.Single(item => item.OriginalName == "retry.png");
        Assert.AreEqual(StoredFileStatus.UploadFailed, failed.Status);
        await recoverable.RetryAttachmentAsync(f.Manager, new(id, failed.Id, "retry.png", "image/png", bytes), "c01-retry", Ct);
        await f.Workspace.SaveRichNoteAsync(f.Manager, new(id, CaseNoteSection.QuickChecks, 0, Image(failed.Id)), "c01-recovered-image", Ct);
        Assert.AreEqual(2, (await f.Workspace.ReadCardAsync(f.Manager, id, Ct)).Attachments.Count);
        CollectionAssert.AreEqual(bytes, (await f.Workspace.ReadAttachmentAsync(f.Manager, failed.Id, Ct)).Content!);
        await f.SetExplicitAccessAsync(f.EmployeeId("manager-phase1@test.invalid"),
            ProcurementTestsHelper.ProcurementManagerAccess(AccessScope.Department) with { ProcurementAccess = ProcurementAccessLevel.Read });
        Assert.AreEqual(2, (await f.Workspace.ReadCardAsync(f.Manager, id, Ct)).RichNotes.Count);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => f.Workspace.SaveRichNoteAsync(f.Manager,
            new(id, CaseNoteSection.Working, 1, Text("read only")), "c01-readonly", Ct));
    }

    [TestMethod]
    public void ServerRebuildsAllowedDocumentAndEncodesText()
    {
        string document = """
            {"type":"doc","onclick":"bad()","content":[
              {"type":"heading","attrs":{"level":2,"style":"color:red"},"content":[{"type":"text","text":"<script>alert(1)</script>"}]},
              {"type":"paragraph","content":[{"type":"text","text":"Расчёт","marks":[{"type":"bold"},{"type":"italic"},{"type":"link","attrs":{"href":"https://example.org/a","onclick":"bad()"}}]}]},
              {"type":"bulletList","content":[{"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"Пункт"}]}]}]},
              {"type":"table","content":[{"type":"tableRow","content":[{"type":"tableCell","content":[{"type":"paragraph","content":[{"type":"text","text":"120 × 10 = 1200"}]}]}]}]}
            ]}
            """;
        var clean = CaseNoteDocument.Validate(document);
        Assert.IsFalse(clean.Html.Contains("<script>", StringComparison.Ordinal));
        Assert.IsFalse(clean.Json.Contains("onclick", StringComparison.Ordinal));
        Assert.IsFalse(clean.Html.Contains("style=", StringComparison.Ordinal));
        StringAssert.Contains(clean.Html, "&lt;script&gt;");
        StringAssert.Contains(clean.Html, "<table><tbody><tr><td>");
        StringAssert.Contains(clean.Html, "<em><strong>");
        Assert.AreEqual(clean, CaseNoteDocument.Validate(clean.Json) with { AttachmentIds = clean.AttachmentIds });
    }

    [TestMethod]
    [DataRow("javascript:alert(1)")]
    [DataRow("data:text/html,test")]
    [DataRow("file:///C:/test")]
    [DataRow("https://user:secret@example.org/")]
    [DataRow("//example.org/")]
    public void UnsafeLinksAreRejected(string href)
    {
        string json = JsonSerializer.Serialize(new { type = "doc", content = new[] { new { type = "paragraph", content = new[] {
            new { type = "text", text = "link", marks = new[] { new { type = "link", attrs = new { href } } } } } } } });
        Assert.ThrowsExactly<ArgumentException>(() => CaseNoteDocument.Validate(json));
    }

    [TestMethod]
    public void UnknownNodesExternalImagesAndOversizedDocumentsAreRejected()
    {
        foreach (string value in new[] {
            """{"type":"doc","content":[{"type":"script"}]}""",
            """{"type":"doc","content":[{"type":"image","attrs":{"src":"data:image/png;base64,abc"}}]}""",
            """{"type":"doc","content":[{"type":"image","attrs":{"src":"https://example.org/a.png"}}]}""",
            """{"type":"doc","content":[{"type":"table","content":[{"type":"tableRow","content":[{"type":"tableCell","attrs":{"colspan":2},"content":[{"type":"paragraph"}]}]}]}]}""",
            Text(new string('x', CaseNoteDocument.MaxBytes)) })
            Assert.ThrowsExactly<ArgumentException>(() => CaseNoteDocument.Validate(value));
    }

    private sealed class FailFirstWriteStorage(IFileStorage inner) : IFileStorage
    {
        private bool failed;
        public Task<FileWriteResult> WriteAsync(Guid stableFileId, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
        {
            if (!failed) { failed = true; throw new IOException("Simulated unavailable storage"); }
            return inner.WriteAsync(stableFileId, content, cancellationToken);
        }
        public Task<byte[]> ReadAsync(string storageKey, CancellationToken cancellationToken) => inner.ReadAsync(storageKey, cancellationToken);
    }
}
