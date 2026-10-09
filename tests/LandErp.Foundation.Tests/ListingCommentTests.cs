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
public sealed class ListingCommentTests
{
    [TestMethod]
    public async Task CreateUpdateNoOpDeleteAndConflictPreserveTriggerHistoryAndActorContext()
    {
        await using var fixture = await ProcurementTests.Phase1Fixture.CreateAsync(true, false);
        ListingCommentService service = Service(fixture);
        Guid listingId = await fixture.CreateUnlinkedManualAsync();
        ListingCommentTypeView type = (await service.ReadAsync(fixture.Manager, listingId,
            CancellationToken.None)).Types.Single(item => item.Name == "Общий комментарий");

        ListingCommentEditorView created = await service.SaveAsync(fixture.Manager,
            new(listingId, type.Id, "Первое значение", null), "comment-create", CancellationToken.None);
        Assert.HasCount(1, created.Comments);
        ListingCommentView first = created.Comments.Single();
        Assert.AreEqual(1L, first.Version);
        Assert.AreEqual(fixture.ManagerEmployeeId, first.CreatedByEmployeeId);

        await using (var raw = new NpgsqlConnection(fixture.Sandbox.RuntimeConnection))
        {
            await raw.OpenAsync();
            await using (var rawNoOp = new NpgsqlCommand(
                "UPDATE catalog.listing_comments SET updated_at = updated_at WHERE id = @id", raw))
            {
                rawNoOp.Parameters.AddWithValue("id", first.Id);
                Assert.AreEqual(1, await rawNoOp.ExecuteNonQueryAsync());
            }
            await using var missingActor = new NpgsqlCommand(
                "UPDATE catalog.listing_comments SET text = 'Недопустимое прямое изменение' WHERE id = @id", raw);
            missingActor.Parameters.AddWithValue("id", first.Id);
            PostgresException actorRequired = await Assert.ThrowsExactlyAsync<PostgresException>(
                () => missingActor.ExecuteNonQueryAsync());
            Assert.AreEqual(PostgresErrorCodes.InvalidParameterValue, actorRequired.SqlState);
        }

        ListingCommentEditorView updated = await service.SaveAsync(fixture.SecondManager,
            new(listingId, type.Id, "Второе значение", first.Version), "comment-update", CancellationToken.None);
        Assert.HasCount(1, updated.Comments);
        ListingCommentView second = updated.Comments.Single();
        Assert.AreEqual(2L, second.Version);
        Guid secondManagerId = fixture.EmployeeId("manager2-phase1@test.invalid");
        Assert.AreEqual(secondManagerId, second.UpdatedByEmployeeId);
        Assert.HasCount(1, updated.HistoryByType[type.Id]);
        ListingCommentHistoryView updateHistory = updated.HistoryByType[type.Id].Single();
        Assert.AreEqual(ListingCommentHistoryOperation.Update, updateHistory.Operation);
        Assert.AreEqual("Первое значение", updateHistory.OldText);
        Assert.AreEqual(fixture.ManagerEmployeeId, updateHistory.OldAuthorEmployeeId);
        Assert.AreEqual(secondManagerId, updateHistory.ChangedByEmployeeId);

        await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(() => service.SaveAsync(fixture.Manager,
            new(listingId, type.Id, "Устаревшая запись", first.Version), "comment-stale", CancellationToken.None));

        ListingCommentEditorView noOp = await service.SaveAsync(fixture.Manager,
            new(listingId, type.Id, "  Второе значение  ", second.Version), "comment-noop", CancellationToken.None);
        Assert.AreEqual(second.Version, noOp.Comments.Single().Version);
        Assert.HasCount(1, noOp.HistoryByType[type.Id]);

        ListingCommentEditorView deleted = await service.SaveAsync(fixture.Manager,
            new(listingId, type.Id, "", second.Version), "comment-delete", CancellationToken.None);
        Assert.HasCount(0, deleted.Comments);
        ListingCommentHistoryView[] history = deleted.HistoryByType[type.Id].ToArray();
        Assert.HasCount(2, history);
        Assert.AreEqual(ListingCommentHistoryOperation.Delete, history[0].Operation);
        Assert.AreEqual("Второе значение", history[0].OldText);
        Assert.AreEqual(secondManagerId, history[0].OldAuthorEmployeeId);
        Assert.AreEqual(fixture.ManagerEmployeeId, history[0].ChangedByEmployeeId);

        await using var pooled = new NpgsqlConnection(fixture.Sandbox.RuntimeConnection);
        await pooled.OpenAsync();
        await using var actor = new NpgsqlCommand(
            "SELECT current_setting('landerp.comment_actor_employee_id', true)", pooled);
        object? leaked = await actor.ExecuteScalarAsync();
        Assert.IsTrue(leaked is null or DBNull || string.IsNullOrEmpty((string)leaked),
            "Transaction-local comment actor must not leak through the connection pool.");

        await using LandErp.Infrastructure.Persistence.LandErpDbContext db = await fixture.Factory.CreateDbContextAsync();
        Assert.AreEqual(1, await db.AuditEvents.CountAsync(item => item.Action == "ListingCommentCreated"));
        Assert.AreEqual(1, await db.AuditEvents.CountAsync(item => item.Action == "ListingCommentUpdated"));
        Assert.AreEqual(1, await db.AuditEvents.CountAsync(item => item.Action == "ListingCommentDeleted"));
    }

    [TestMethod]
    public async Task TypeManagementRightsArchiveAndOrganizationBoundaryKeepUsedValues()
    {
        await using var fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        ListingCommentService service = Service(fixture);
        Guid listingId = await fixture.CreateUnlinkedManualAsync();

        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => service.SaveTypeAsync(fixture.Manager,
            new(null, null, "Менеджерский вид", null, 20), "type-denied", CancellationToken.None));
        ListingCommentTypeView headType = await service.SaveTypeAsync(fixture.Head,
            new(null, null, "Переговоры", "Что сообщил продавец", 20), "type-head", CancellationToken.None);
        ListingCommentTypeView ownerType = await service.SaveTypeAsync(fixture.Owner,
            new(null, null, "Риск", null, 30), "type-owner", CancellationToken.None);

        var structure = await fixture.Organization.ReadAsync(fixture.Owner, CancellationToken.None);
        Guid administratorUserId = await ProcurementTestsHelper.InviteAsync(fixture.Services,
            fixture.Organization, fixture.Owner, structure, "Administrator", "administrator-comments@test.invalid",
            "Administrator", fixture.DepartmentA, AccessScope.Organization, EmployeeAccessRules.NoAccess);
        Subject administrator = new(administratorUserId, true);
        ListingCommentTypeView administratorType = await service.SaveTypeAsync(administrator,
            new(null, null, "Юридический", null, 40), "type-admin", CancellationToken.None);
        Assert.AreEqual("Юридический", administratorType.Name);

        ListingCommentEditorView withValue = await service.SaveAsync(fixture.Manager,
            new(listingId, headType.Id, "Продавец готов обсуждать цену", null), "comment-used", CancellationToken.None);
        ListingCommentView value = withValue.Comments.Single(item => item.CommentTypeId == headType.Id);
        ListingCommentTypeView archived = await service.SetTypeActiveAsync(fixture.Head,
            new(headType.Id, headType.Version, false), "type-archive", CancellationToken.None);
        Assert.IsFalse(archived.IsActive);
        ListingCommentEditorView afterArchive = await service.ReadAsync(fixture.Manager, listingId, CancellationToken.None);
        Assert.IsFalse(afterArchive.Types.Single(item => item.Id == headType.Id).IsActive);
        Assert.AreEqual(value.Text, afterArchive.Comments.Single(item => item.CommentTypeId == headType.Id).Text);

        Guid otherListing = await fixture.CreateUnlinkedManualAsync();
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.SaveAsync(fixture.Manager,
            new(otherListing, headType.Id, "Новое значение архивного вида", null), "comment-inactive", CancellationToken.None));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => service.ReadAsync(fixture.ForeignOwner,
            listingId, CancellationToken.None));

        await fixture.SetExplicitAccessAsync(fixture.ManagerEmployeeId,
            EmployeeAccessRules.NoAccess with { IncomingAccess = IncomingAccessLevel.Read });
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => service.SaveAsync(fixture.Manager,
            new(listingId, ownerType.Id, "Нет права", null), "comment-read-only", CancellationToken.None));

        await using var connection = new NpgsqlConnection(fixture.Sandbox.RuntimeConnection);
        await connection.OpenAsync();
        await using var duplicate = new NpgsqlCommand("""
            INSERT INTO catalog.listing_comments
                (id, organization_id, listing_id, comment_type_id, text,
                 created_by_employee_id, created_at, updated_by_employee_id, updated_at, version)
            SELECT gen_random_uuid(), organization_id, listing_id, comment_type_id, 'Дубль',
                created_by_employee_id, now(), updated_by_employee_id, now(), 1
            FROM catalog.listing_comments WHERE id = @id
            """, connection);
        duplicate.Parameters.AddWithValue("id", value.Id);
        PostgresException unique = await Assert.ThrowsExactlyAsync<PostgresException>(
            () => duplicate.ExecuteNonQueryAsync());
        Assert.AreEqual(PostgresErrorCodes.UniqueViolation, unique.SqlState);
    }

    [TestMethod]
    public async Task CommentSearchRunsBeforePaginationAndPageCommentsAreLoadedInOneProjection()
    {
        await using var fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        ListingCommentService service = Service(fixture);
        Guid matchingListing = await fixture.CreateUnlinkedManualAsync();
        ListingCommentTypeView type = (await service.ReadAsync(fixture.Manager, matchingListing,
            CancellationToken.None)).Types.Single(item => item.Name == "Общий комментарий");
        await service.SaveAsync(fixture.Manager, new(matchingListing, type.Id,
            "alphaunique подъезд требует проверки", null), "comment-search", CancellationToken.None);
        _ = await fixture.CreateUnlinkedManualAsync(); // Newer row would occupy page 1 if filtering happened after pagination.

        IncomingCatalogReadService reads = new(fixture.Factory, fixture.Workspace, TimeProvider.System);
        IncomingCatalogReadPage page = await reads.ReadAsync(fixture.Manager,
            new(new(Text: "alphaunique", Offset: 0, Size: 1)), CancellationToken.None);
        Assert.AreEqual(1, page.Total);
        Assert.HasCount(1, page.Items);
        Assert.AreEqual(matchingListing, page.Items.Single().Id);
        IncomingCatalogRowRead row = page.Rows[matchingListing];
        Assert.HasCount(1, row.Comments!);
        ListingCommentPreview preview = row.Comments!.Single();
        Assert.AreEqual("Общий комментарий", preview.TypeName);
        StringAssert.Contains(preview.Text, "alphaunique");

        string source = await File.ReadAllTextAsync(Path.Combine(FoundationTests.RepositoryRoot(),
            "src", "LandErp.Infrastructure", "Modules", "Catalog", "IncomingCatalogReadService.cs"));
        Assert.IsTrue(source.IndexOf("commentRows", StringComparison.Ordinal)
            < source.IndexOf("Dictionary<Guid, IncomingCatalogRowRead>", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("foreach (Listing item", StringComparison.Ordinal)
            && source.Contains("ListingComments", StringComparison.Ordinal));
        string ui = await File.ReadAllTextAsync(Path.Combine(FoundationTests.RepositoryRoot(),
            "src", "LandErp.Server", "Components", "Pages", "IncomingCatalogV2.razor"));
        StringAssert.Contains(ui, "<th>Комментарии</th>");
        StringAssert.Contains(ui, "OpenCommentsAsync(item)");
        StringAssert.Contains(ui, "HistoryByType");
        StringAssert.Contains(ui, "Управление видами");
    }

    private static ListingCommentService Service(ProcurementTests.Phase1Fixture fixture) =>
        new(fixture.Factory, new EmployeeAccessService(fixture.Factory), TimeProvider.System);
}
