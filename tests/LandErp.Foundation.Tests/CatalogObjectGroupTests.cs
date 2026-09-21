using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Infrastructure.Modules.Catalog;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
[TestCategory("PostgreSQL")]
public sealed class CatalogObjectGroupTests
{
    [TestMethod]
    public async Task FourSourcesShareOneHeadlessGroupAndOnePropertyCase()
    {
        await using ProcurementTests.Phase1Fixture fixture =
            await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        IncomingCatalogReadService reads = new(fixture.Factory, fixture.Workspace, TimeProvider.System);
        const string cadastral = "50:27:0020549:439";

        Guid first = await CreateAsync(fixture, CatalogSource.Referral, "Источник один", cadastral, "group-a");
        Guid second = await CreateAsync(fixture, CatalogSource.Telegram, "Источник два", cadastral, "group-b");
        await ConfirmOnlyCandidateAsync(fixture, reads, second, "confirm-b");

        Guid third = await CreateAsync(fixture, CatalogSource.Agent, "Источник три", cadastral, "group-c");
        await ConfirmOnlyCandidateAsync(fixture, reads, third, "confirm-c");

        Guid fourth = await CreateAsync(fixture, CatalogSource.DirectOwner, "Источник четыре", cadastral, "group-d");
        await ConfirmOnlyCandidateAsync(fixture, reads, fourth, "confirm-d");

        await using (LandErpDbContext db = await fixture.Factory.CreateDbContextAsync())
        {
            var members = await db.Listings.AsNoTracking()
                .Where(item => new[] { first, second, third, fourth }.Contains(item.Id))
                .Select(item => new { item.Id, item.ObjectGroupId }).ToArrayAsync();
            Guid groupId = members.Select(item => item.ObjectGroupId).Distinct().Single()
                ?? throw new AssertFailedException("Confirmed duplicates must have an object group.");
            Assert.AreEqual(4, members.Length);
            Assert.AreEqual(1, await db.CatalogObjectGroups.CountAsync(item => item.Id == groupId));
            Assert.AreEqual(4, await db.Listings.CountAsync(item => item.ObjectGroupId == groupId));
        }

        IncomingCatalogDetailRead grouped = await reads.ReadDetailAsync(fixture.Manager, first, CancellationToken.None);
        Assert.IsNotNull(grouped.ObjectGroup);
        Assert.AreEqual(4, grouped.ObjectGroup.MemberCount);
        CollectionAssert.AreEquivalent(new[] { first, second, third, fourth },
            grouped.ObjectGroup.Members.Select(item => item.CatalogItemId).ToArray());

        TakeToWorkResult taken = await fixture.Workspace.TakeToWorkAsync(
            fixture.Manager, new(fourth), "take-group", CancellationToken.None);
        Assert.IsTrue(taken.Created);

        await using (LandErpDbContext db = await fixture.Factory.CreateDbContextAsync())
        {
            Assert.AreEqual(4, await db.PropertyCaseSourceLinks.CountAsync(item =>
                item.PropertyCaseId == taken.CaseId && item.Confirmed));
            Assert.AreEqual(4, await db.Listings.CountAsync(item =>
                new[] { first, second, third, fourth }.Contains(item.Id)
                && item.Disposition == CatalogDisposition.InWork));
        }
    }

    [TestMethod]
    public async Task UnlinkAfterTakeToWorkDetachesOnlyThatSourceAndRejectedPairDoesNotReturn()
    {
        await using ProcurementTests.Phase1Fixture fixture =
            await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        IncomingCatalogReadService reads = new(fixture.Factory, fixture.Workspace, TimeProvider.System);
        const string cadastral = "50:08:0060201:77";

        Guid first = await CreateAsync(fixture, CatalogSource.Referral, "Первый источник", cadastral, "unlink-a");
        Guid second = await CreateAsync(fixture, CatalogSource.Telegram, "Второй источник", cadastral, "unlink-b");
        await ConfirmOnlyCandidateAsync(fixture, reads, second, "unlink-confirm");
        TakeToWorkResult taken = await fixture.Workspace.TakeToWorkAsync(
            fixture.Manager, new(first), "unlink-take", CancellationToken.None);

        CatalogItemDetail secondBefore = await fixture.Workspace.ReadItemAsync(
            fixture.Manager, second, CancellationToken.None);
        await fixture.Workspace.UnlinkCatalogItemFromObjectGroupAsync(fixture.Manager,
            new(second, secondBefore.Item.Version, "После проверки найден другой кадастровый контур"),
            "unlink-source", CancellationToken.None);

        await using (LandErpDbContext db = await fixture.Factory.CreateDbContextAsync())
        {
            Listing firstRow = await db.Listings.AsNoTracking().SingleAsync(item => item.Id == first);
            Listing secondRow = await db.Listings.AsNoTracking().SingleAsync(item => item.Id == second);
            Assert.IsNull(firstRow.ObjectGroupId, "A two-member group must dissolve after one member is removed.");
            Assert.IsNull(secondRow.ObjectGroupId);
            Assert.AreEqual(CatalogDisposition.InWork, firstRow.Disposition);
            Assert.AreEqual(CatalogDisposition.Incoming, secondRow.Disposition);
            Assert.AreEqual(0, await db.CatalogObjectGroups.CountAsync());
            Assert.AreEqual(1, await db.PropertyCaseSourceLinks.CountAsync(item =>
                item.PropertyCaseId == taken.CaseId && item.Confirmed && item.CatalogItemId == first));
            Assert.AreEqual(0, await db.PropertyCaseSourceLinks.CountAsync(item =>
                item.PropertyCaseId == taken.CaseId && item.Confirmed && item.CatalogItemId == second));
            Assert.AreEqual(DuplicateCandidateStatus.Rejected,
                await db.CatalogDuplicateCandidates.Where(item =>
                        item.OrganizationId == fixture.OrganizationId
                        && ((item.ListingId == first && item.CandidateListingId == second)
                            || (item.ListingId == second && item.CandidateListingId == first)))
                    .Select(item => item.Status).SingleAsync());
        }

        IncomingDuplicateMatchingMaintenance matcher = new(fixture.Factory, TimeProvider.System);
        await matcher.RefreshAsync(second, CancellationToken.None);
        IncomingCatalogReadPage possible = await reads.ReadAsync(fixture.Manager,
            new(new(), Preset: IncomingCatalogPreset.PossibleDuplicate), CancellationToken.None);
        Assert.IsFalse(possible.Items.Any(item => item.Id == second),
            "A pair explicitly separated by the manager must not be offered again.");
    }

    [TestMethod]
    public async Task ManualLinkCreatesSameObjectGroupAndGenericDuplicateClassificationIsBlocked()
    {
        await using ProcurementTests.Phase1Fixture fixture =
            await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        IncomingCatalogReadService reads = new(fixture.Factory, fixture.Workspace, TimeProvider.System);

        Guid first = await fixture.Workspace.CreateManualAsync(fixture.Manager,
            new(CatalogSource.Referral, "Лесной участок", "Истра", 4_500_000m, 900m,
                null, null, null, "Первое объявление без общих идентификаторов", "manual-group-a"),
            "manual-group-a", CancellationToken.None);
        Guid second = await fixture.Workspace.CreateManualAsync(fixture.Manager,
            new(CatalogSource.Telegram, "Участок возле леса", "Истринский округ", 4_650_000m, 910m,
                null, null, null, "Второе объявление подтверждено менеджером вручную", "manual-group-b"),
            "manual-group-b", CancellationToken.None);

        CatalogItemDetail before = await fixture.Workspace.ReadItemAsync(fixture.Manager, second, CancellationToken.None);
        await fixture.Workspace.LinkCatalogItemsAsSameObjectAsync(fixture.Manager,
            new(second, before.Item.Version, first, "Созвонились с продавцом и подтвердили один участок"),
            "manual-link", CancellationToken.None);

        IncomingCatalogDetailRead grouped = await reads.ReadDetailAsync(fixture.Manager, second, CancellationToken.None);
        Assert.IsNotNull(grouped.ObjectGroup);
        Assert.AreEqual(2, grouped.ObjectGroup.MemberCount);

        await using (LandErpDbContext db = await fixture.Factory.CreateDbContextAsync())
        {
            CatalogDuplicateCandidate decision = await db.CatalogDuplicateCandidates.AsNoTracking().SingleAsync(item =>
                item.OrganizationId == fixture.OrganizationId
                && ((item.ListingId == first && item.CandidateListingId == second)
                    || (item.ListingId == second && item.CandidateListingId == first)));
            Assert.AreEqual(DuplicateCandidateStatus.Confirmed, decision.Status);
        }

        CatalogItemDetail after = await fixture.Workspace.ReadItemAsync(fixture.Manager, second, CancellationToken.None);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.Workspace.SetDispositionAsync(
            fixture.Manager,
            new(second, after.Item.Version, CatalogDisposition.Duplicate, "Нельзя создавать несвязанный дубль"),
            "generic-duplicate-blocked", CancellationToken.None));
    }

    private static Task<Guid> CreateAsync(ProcurementTests.Phase1Fixture fixture, CatalogSource source,
        string title, string cadastral, string correlationId) =>
        fixture.Workspace.CreateManualAsync(fixture.Manager,
            new(source, title, "Московская область", 6_500_000m, 601m,
                null, null, cadastral, "Один и тот же физический участок", correlationId),
            correlationId, CancellationToken.None);

    private static async Task ConfirmOnlyCandidateAsync(ProcurementTests.Phase1Fixture fixture,
        IncomingCatalogReadService reads, Guid listingId, string correlationId)
    {
        IncomingCatalogDetailRead detail = await reads.ReadDetailAsync(fixture.Manager, listingId, CancellationToken.None);
        IncomingDuplicateCandidateView candidate = detail.DuplicateCandidates!.Single();
        await fixture.Workspace.ReviewDuplicateCandidateAsync(fixture.Manager,
            new(candidate.Id, candidate.Version, true), correlationId, CancellationToken.None);
    }
}
