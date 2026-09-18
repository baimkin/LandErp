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
public sealed class ReleasePackageB202Tests
{
    [TestMethod]
    public async Task RelinkMovesConfirmedSourceAndPreservesOldRelationHistory()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        Guid sourceId = await fixture.CreateUnlinkedManualAsync();
        TakeToWorkResult first = await fixture.Workspace.TakeToWorkAsync(fixture.Manager, new(sourceId), "b202-take", CancellationToken.None);
        ManualPropertyCaseResult second = await fixture.Workspace.CreateManualCaseAsync(fixture.Manager,
            new("Правильный объект", "Химки", null, null, null, "B2-02 target"), "b202-target", CancellationToken.None);

        CatalogItemDetail source = await fixture.Workspace.ReadItemAsync(fixture.Manager, sourceId, CancellationToken.None);
        await fixture.Workspace.CorrectCaseLinkAsync(fixture.Manager,
            new(sourceId, source.Item.Version, first.CaseId, second.CaseId, "Источник ошибочно привязали к другому объекту"),
            "b202-relink", CancellationToken.None);

        await using (LandErpDbContext db = await fixture.Factory.CreateDbContextAsync())
        {
            PropertyCaseSourceLink[] links = await db.PropertyCaseSourceLinks.AsNoTracking()
                .Where(item => item.CatalogItemId == sourceId).OrderBy(item => item.RecordedAt).ToArrayAsync();
            Assert.AreEqual(2, links.Length);
            Assert.AreEqual(1, links.Count(item => item.Confirmed));
            Assert.IsFalse(links.Single(item => item.PropertyCaseId == first.CaseId).Confirmed);
            Assert.IsTrue(links.Single(item => item.PropertyCaseId == second.CaseId).Confirmed);
        }

        CatalogItemDetail updated = await fixture.Workspace.ReadItemAsync(fixture.Manager, sourceId, CancellationToken.None);
        Assert.AreEqual(second.CaseId, updated.Item.PropertyCaseId);
        Assert.AreEqual(CatalogDisposition.InWork, updated.Item.Disposition);
        Assert.IsFalse(updated.Item.AttentionRequired);
        Assert.AreEqual(0, (await fixture.Workspace.ReadCardAsync(fixture.Manager, first.CaseId, CancellationToken.None)).Sources.Count);
        Assert.AreEqual(1, (await fixture.Workspace.ReadCardAsync(fixture.Manager, second.CaseId, CancellationToken.None)).Sources.Count);

        CaseCard oldCard = await fixture.Workspace.ReadCardAsync(fixture.Manager, first.CaseId, CancellationToken.None);
        CaseCard newCard = await fixture.Workspace.ReadCardAsync(fixture.Manager, second.CaseId, CancellationToken.None);
        TimelineItem unlink = oldCard.Timeline.First(item => item.Kind == "SourceUnlinked");
        TimelineItem relink = newCard.Timeline.First(item => item.Kind == "SourceRelinked");
        StringAssert.Contains(unlink.Body, "Источник ошибочно привязали к другому объекту");
        StringAssert.Contains(relink.Body, first.BusinessNumber);

        await using LandErpDbContext auditDb = await fixture.Factory.CreateDbContextAsync();
        var audit = await auditDb.AuditEvents.AsNoTracking().SingleAsync(item =>
            item.ObjectId == first.CaseId && item.Action == "PropertyCaseSourceLinkCorrected");
        StringAssert.Contains(audit.Changes, second.CaseId.ToString());
        StringAssert.Contains(audit.Changes, "Источник ошибочно привязали к другому объекту");
    }

    [TestMethod]
    public async Task UnlinkReturnsSourceToIncomingAndKeepsHistoricalRow()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        Guid sourceId = await fixture.CreateUnlinkedManualAsync();
        TakeToWorkResult taken = await fixture.Workspace.TakeToWorkAsync(fixture.Manager, new(sourceId), "b202-take", CancellationToken.None);
        CatalogItemDetail source = await fixture.Workspace.ReadItemAsync(fixture.Manager, sourceId, CancellationToken.None);

        await fixture.Workspace.CorrectCaseLinkAsync(fixture.Manager,
            new(sourceId, source.Item.Version, taken.CaseId, null, "Источник относится к другому объекту, цель пока не определена"),
            "b202-unlink", CancellationToken.None);

        CatalogItemDetail updated = await fixture.Workspace.ReadItemAsync(fixture.Manager, sourceId, CancellationToken.None);
        Assert.IsNull(updated.Item.PropertyCaseId);
        Assert.AreEqual(CatalogDisposition.Incoming, updated.Item.Disposition);
        Assert.IsTrue(updated.Item.AttentionRequired);

        await using LandErpDbContext db = await fixture.Factory.CreateDbContextAsync();
        PropertyCaseSourceLink historical = await db.PropertyCaseSourceLinks.AsNoTracking()
            .SingleAsync(item => item.CatalogItemId == sourceId);
        Assert.IsFalse(historical.Confirmed);
        Assert.AreEqual(taken.CaseId, historical.PropertyCaseId);
    }

    [TestMethod]
    public async Task StaleCorrectionCannotCreateTwoConfirmedRelations()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        Guid sourceId = await fixture.CreateUnlinkedManualAsync();
        TakeToWorkResult first = await fixture.Workspace.TakeToWorkAsync(fixture.Manager, new(sourceId), "b202-take", CancellationToken.None);
        ManualPropertyCaseResult second = await fixture.Workspace.CreateManualCaseAsync(fixture.Manager,
            new("Второй объект", null, null, null, null, "B2-02"), "b202-second", CancellationToken.None);
        ManualPropertyCaseResult third = await fixture.Workspace.CreateManualCaseAsync(fixture.Manager,
            new("Третий объект", null, null, null, null, "B2-02"), "b202-third", CancellationToken.None);
        CatalogItemDetail source = await fixture.Workspace.ReadItemAsync(fixture.Manager, sourceId, CancellationToken.None);

        await fixture.Workspace.CorrectCaseLinkAsync(fixture.Manager,
            new(sourceId, source.Item.Version, first.CaseId, second.CaseId, "Первая корректировка"),
            "b202-first", CancellationToken.None);

        await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(() => fixture.Workspace.CorrectCaseLinkAsync(fixture.Manager,
            new(sourceId, source.Item.Version, first.CaseId, third.CaseId, "Устаревшая корректировка"),
            "b202-stale", CancellationToken.None));

        await using LandErpDbContext db = await fixture.Factory.CreateDbContextAsync();
        PropertyCaseSourceLink[] confirmed = await db.PropertyCaseSourceLinks.AsNoTracking()
            .Where(item => item.CatalogItemId == sourceId && item.Confirmed).ToArrayAsync();
        Assert.AreEqual(1, confirmed.Length);
        Assert.AreEqual(second.CaseId, confirmed[0].PropertyCaseId);
    }

    [TestMethod]
    public async Task RelinkBackReactivatesHistoricalPairInsteadOfCreatingDuplicate()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        Guid sourceId = await fixture.CreateUnlinkedManualAsync();
        TakeToWorkResult first = await fixture.Workspace.TakeToWorkAsync(fixture.Manager, new(sourceId), "b202-take", CancellationToken.None);
        ManualPropertyCaseResult second = await fixture.Workspace.CreateManualCaseAsync(fixture.Manager,
            new("Второй объект", null, null, null, null, "B2-02"), "b202-second", CancellationToken.None);

        CatalogItemDetail source = await fixture.Workspace.ReadItemAsync(fixture.Manager, sourceId, CancellationToken.None);
        await fixture.Workspace.CorrectCaseLinkAsync(fixture.Manager,
            new(sourceId, source.Item.Version, first.CaseId, second.CaseId, "Исправить на второй объект"),
            "b202-to-second", CancellationToken.None);

        source = await fixture.Workspace.ReadItemAsync(fixture.Manager, sourceId, CancellationToken.None);
        await fixture.Workspace.CorrectCaseLinkAsync(fixture.Manager,
            new(sourceId, source.Item.Version, second.CaseId, first.CaseId, "Повторная проверка подтвердила первый объект"),
            "b202-back", CancellationToken.None);

        await using LandErpDbContext db = await fixture.Factory.CreateDbContextAsync();
        PropertyCaseSourceLink[] links = await db.PropertyCaseSourceLinks.AsNoTracking()
            .Where(item => item.CatalogItemId == sourceId).ToArrayAsync();
        Assert.AreEqual(2, links.Length, "Historical pair must be reactivated, not duplicated.");
        Assert.AreEqual(1, links.Count(item => item.Confirmed));
        Assert.IsTrue(links.Single(item => item.PropertyCaseId == first.CaseId).Confirmed);
    }

    [TestMethod]
    public async Task NormalLinkFlowReactivatesHistoricalPairAfterUnlink()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        Guid sourceId = await fixture.CreateUnlinkedManualAsync();
        TakeToWorkResult first = await fixture.Workspace.TakeToWorkAsync(fixture.Manager, new(sourceId), "b202-take", CancellationToken.None);
        CatalogItemDetail source = await fixture.Workspace.ReadItemAsync(fixture.Manager, sourceId, CancellationToken.None);

        await fixture.Workspace.CorrectCaseLinkAsync(fixture.Manager,
            new(sourceId, source.Item.Version, first.CaseId, null, "Временно отвязать источник"),
            "b202-unlink", CancellationToken.None);

        TakeToWorkResult linked = await fixture.Workspace.TakeToWorkAsync(fixture.Manager,
            new(sourceId, first.CaseId), "b202-normal-link", CancellationToken.None);
        Assert.AreEqual(first.CaseId, linked.CaseId);
        Assert.IsFalse(linked.Created);

        await using LandErpDbContext db = await fixture.Factory.CreateDbContextAsync();
        PropertyCaseSourceLink[] links = await db.PropertyCaseSourceLinks.AsNoTracking()
            .Where(item => item.CatalogItemId == sourceId).ToArrayAsync();
        Assert.AreEqual(1, links.Length);
        Assert.IsTrue(links[0].Confirmed);
    }

    [TestMethod]
    public async Task CorrectionRequiresReasonAndManagerPermission()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        Guid sourceId = await fixture.CreateUnlinkedManualAsync();
        TakeToWorkResult taken = await fixture.Workspace.TakeToWorkAsync(fixture.Manager, new(sourceId), "b202-take", CancellationToken.None);
        CatalogItemDetail source = await fixture.Workspace.ReadItemAsync(fixture.Manager, sourceId, CancellationToken.None);

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.Workspace.CorrectCaseLinkAsync(fixture.Manager,
            new(sourceId, source.Item.Version, taken.CaseId, null, " "),
            "b202-no-reason", CancellationToken.None));

        Subject unknown = new(Guid.CreateVersion7(), false);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => fixture.Workspace.CorrectCaseLinkAsync(unknown,
            new(sourceId, source.Item.Version, taken.CaseId, null, "Попытка без прав"),
            "b202-denied", CancellationToken.None));
    }
}
