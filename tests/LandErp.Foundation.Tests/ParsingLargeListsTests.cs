using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.Collection.Contracts;
using LandErp.Application.Modules.Collection.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Infrastructure.Modules.Collection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass, TestCategory("PostgreSQL")]
public sealed class ParsingLargeListsTests
{
    [TestMethod]
    public async Task HistoryCursorFiltersAndPermissionsCoverAllRuns()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        var admin = new CollectionAdministration(f.Factory, TimeProvider.System);
        Guid zone = await admin.CreateGroupAsync(f.Owner, "Зона %_", 0, "history", default);
        await admin.CreateSearchAsync(f.Owner, new("Новый поиск", CatalogSource.Avito,
            "https://www.avito.ru/moskva/zemelnye_uchastki", 10, zone), "history", default);
        await admin.CreateSearchAsync(f.Owner, new("Архивный уникальный", CatalogSource.Cian,
            "https://www.cian.ru/cat.php", 10, zone), "history", default);
        var searches = (await admin.ReadAsync(f.Owner, default)).Searches;
        Guid search = searches.Single(s => s.Label == "Новый поиск").Id;
        Guid old = searches.Single(s => s.Label == "Архивный уникальный").Id;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var expected = new HashSet<Guid>();
        await using (var db = f.Sandbox.Context())
        {
            for (int i = 0; i < 175; i++)
            {
                Guid id = Guid.CreateVersion7(); expected.Add(id);
                db.CollectionJobs.Add(new() { Id = id, OrganizationId = f.OrganizationId, SearchId = search,
                    CreatedAt = now, State = CollectionJobState.Completed, ResultCode = "Success" });
            }
            Guid oldId = Guid.CreateVersion7(); expected.Add(oldId);
            db.CollectionJobs.Add(new() { Id = oldId, OrganizationId = f.OrganizationId, SearchId = old,
                CreatedAt = now.AddDays(-30), State = CollectionJobState.Failed, ResultCode = "SourceError" });
            await db.SaveChangesAsync();
        }
        var page = await admin.ReadHistoryAsync(f.Owner, new(), default);
        Assert.HasCount(50, page.Items); Assert.AreEqual(176, page.Total); Assert.IsNotNull(page.Next);
        var actual = page.Items.Select(j => j.Id).ToList();
        // An insertion between pages must not shift the cursor, even when all initial times match.
        Guid newerId = Guid.CreateVersion7();
        await using (var db = f.Sandbox.Context())
        {
            db.CollectionJobs.Add(new() { Id = newerId, OrganizationId = f.OrganizationId, SearchId = search,
                CreatedAt = now.AddMinutes(1), State = CollectionJobState.Pending });
            await db.SaveChangesAsync();
        }
        while (page.Next is not null)
        {
            page = await admin.ReadHistoryAsync(f.Owner, new(Before: page.Next), default);
            Assert.AreEqual(177, page.Total);
            Assert.IsTrue(page.Items.Count <= 50);
            actual.AddRange(page.Items.Select(j => j.Id));
        }
        Assert.HasCount(expected.Count, actual);
        CollectionAssert.AreEquivalent(expected.ToArray(), actual.ToArray());
        Assert.IsFalse(actual.Contains(newerId));
        Assert.HasCount(100, (await admin.ReadAsync(f.Owner, default)).Jobs);
        Assert.HasCount(0, (await admin.ReadAsync(f.Owner, default, includeRecentJobs: false)).Jobs);
        Assert.AreEqual(1, (await admin.ReadHistoryAsync(f.Owner, new(Text: "уникальный"), default)).Total);
        Assert.AreEqual(1, (await admin.ReadHistoryAsync(f.Owner, new(Source: CatalogSource.Cian), default)).Total);
        Assert.AreEqual(1, (await admin.ReadHistoryAsync(f.Owner, new(Result: CollectionHistoryResult.Attention), default)).Total);
        Assert.AreEqual(1, (await admin.ReadHistoryAsync(f.Owner, new(Result: CollectionHistoryResult.Running), default)).Total);
        Assert.AreEqual(175, (await admin.ReadHistoryAsync(f.Owner, new(Result: CollectionHistoryResult.Completed), default)).Total);
        Assert.AreEqual(177, (await admin.ReadHistoryAsync(f.Owner, new(Text: "%_"), default)).Total);
        Assert.AreEqual(0, (await admin.ReadHistoryAsync(f.Owner, new(Text: "missing%_"), default)).Total);
        Assert.AreEqual(0, (await admin.ReadHistoryAsync(f.ForeignOwner, new(), default)).Total);
        await f.SetExplicitAccessAsync(f.ManagerEmployeeId, EmployeeAccessRules.NoAccess with { CanManageSearchGroups = true });
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => admin.ReadHistoryAsync(f.Manager, new(), default));
        await f.SetExplicitAccessAsync(f.ManagerEmployeeId, EmployeeAccessRules.NoAccess with { CollectionAccess = CollectionAccessLevel.Read });
        Assert.AreEqual(177, (await admin.ReadHistoryAsync(f.Manager, new(), default)).Total);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => admin.ReadHistoryAsync(f.Owner, new(Size: 1001), default));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => admin.ReadHistoryAsync(f.Owner, new(Text: new string('x', 201)), default));
    }
}
