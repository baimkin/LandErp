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
public sealed class IncomingArchiveSortingTests
{
    private static IncomingFilterPresetCriteriaV1 Criteria(IncomingCatalogSortField sort = IncomingCatalogSortField.ChangedAt)
        => new(2, null, null, CatalogDisposition.Incoming, CatalogAgeRange.Any, null, null, null, null,
            null, null, [], false, sort, IncomingCatalogSortDirection.Ascending);

    [TestMethod]
    public async Task PricePerSotkaSortsBeforePaginationNullsLastAndRoundTripsSavedOrder()
    {
        await using var fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        await using var source = NpgsqlDataSource.Create(fixture.Sandbox.RuntimeConnection);
        IncomingFilterPresetService presets = new(source, new EmployeeAccessService(fixture.Factory));
        IncomingCatalogReadService reads = new(fixture.Factory, new EmployeeAccessService(fixture.Factory), fixture.Workspace, TimeProvider.System, presets);
        List<Guid> ids = [];
        (decimal? Price, decimal? Area)[] values = [(null, 100m), (3000m, 100m), (2000m, 200m),
            (4000m, 200m), (1000m, 100m), (5000m, null), (5000m, 0m), (5000m, -1m), (0m, 100m)];
        foreach (var value in values)
            ids.Add(await fixture.Workspace.CreateManualAsync(fixture.Manager,
                new(CatalogSource.Telegram, $"Сортировка {ids.Count}", "Химки", value.Price, value.Area,
                    null, null, null, null, "F03 сортировка"), "f03", CancellationToken.None));
        foreach (var direction in Enum.GetValues<IncomingCatalogSortDirection>())
        {
            List<CatalogItemView> rows = [];
            for (int offset = 0; offset < values.Length; offset += 2)
            {
                var page = await reads.ReadAsync(fixture.Manager, new(new(Offset: offset, Size: 2),
                    SortField: IncomingCatalogSortField.PricePerSotka, SortDirection: direction,
                    WorkingScope: new(IncomingCatalogMode.AllListings)), CancellationToken.None);
                Assert.AreEqual(values.Length, page.Total);
                rows.AddRange(page.Items);
            }
            Guid[] ties = new[] { ids[2], ids[4] }.Order().ToArray();
            Guid[] expected = direction == IncomingCatalogSortDirection.Ascending
                ? [.. ties, ids[3], ids[1]] : [ids[1], ids[3], .. ties];
            CollectionAssert.AreEqual(expected, rows.Take(4).Select(item => item.Id).ToArray());
            Assert.IsTrue(rows.Skip(4).All(item => item.PricePerSotka == null));
            CollectionAssert.AreEqual(ids.Where((_, index) => index is 0 or 5 or 6 or 7 or 8).Order().ToArray(),
                rows.Skip(4).Select(item => item.Id).ToArray());
            Assert.AreEqual(values.Length, rows.Select(item => item.Id).Distinct().Count());
        }

        var saved = await presets.CreateAsync(fixture.Manager, new(null, "Цена сотки", Criteria(IncomingCatalogSortField.PricePerSotka)), CancellationToken.None);
        var restored = (await presets.ReadAsync(fixture.Manager, CancellationToken.None)).Single();
        Assert.AreEqual(IncomingCatalogSortField.PricePerSotka, restored.Criteria.SortField);
        Assert.AreEqual(IncomingCatalogSortDirection.Ascending, restored.Criteria.SortDirection);
        var updated = await presets.UpdateAsync(fixture.Manager, new(saved.Id, saved.Version, saved.Name,
            restored.Criteria with { SortDirection = IncomingCatalogSortDirection.Descending }), CancellationToken.None);
        restored = (await presets.ReadAsync(fixture.Manager, CancellationToken.None)).Single();
        Assert.AreEqual(updated.Id, restored.Id);
        var restoredPage = await reads.ReadAsync(fixture.Manager, new(new(Size: 2),
            SortField: restored.Criteria.SortField, SortDirection: restored.Criteria.SortDirection,
            WorkingScope: new(SelectedPresetId: restored.Id)), CancellationToken.None);
        CollectionAssert.AreEqual(new[] { ids[1], ids[3] }, restoredPage.Items.Select(item => item.Id).ToArray());

        var old = await presets.CreateAsync(fixture.Manager, new(null, "Старая сортировка", Criteria(IncomingCatalogSortField.Price)), CancellationToken.None);
        await using (var connection = new NpgsqlConnection(fixture.Sandbox.MigratorConnection))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("UPDATE catalog.incoming_filter_presets SET criteria_json = jsonb_set(criteria_json, '{schemaVersion}', '1') WHERE id = @id", connection);
            command.Parameters.AddWithValue("id", old.Id);
            await command.ExecuteNonQueryAsync();
        }
        Assert.AreEqual(IncomingCatalogSortField.Price, (await presets.ReadAsync(fixture.Manager, CancellationToken.None))
            .Single(item => item.Id == old.Id).Criteria.SortField);
    }

    [TestMethod]
    public async Task ArchiveUsesOnlyClassifiedStatesIgnoresWorkingCriteriaAndPreservesGroupAndReadIsolation()
    {
        await using var fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        await using var source = NpgsqlDataSource.Create(fixture.Sandbox.RuntimeConnection);
        IncomingFilterPresetService presets = new(source, new EmployeeAccessService(fixture.Factory));
        IncomingCatalogReadService reads = new(fixture.Factory, new EmployeeAccessService(fixture.Factory), fixture.Workspace, TimeProvider.System, presets);
        Dictionary<CatalogDisposition, Guid> ids = [];
        foreach (var state in Enum.GetValues<CatalogDisposition>())
        {
            Guid id = await fixture.Workspace.CreateManualAsync(fixture.Manager,
                new(CatalogSource.Telegram, $"Архив {state}", "Химки", 1000m, 100m,
                    null, null, null, null, "F03 архив"), "f03", CancellationToken.None);
            ids[state] = id;
            if (state == CatalogDisposition.Dismissed)
            {
                var created = await fixture.Workspace.ReadItemAsync(fixture.Manager, id, CancellationToken.None);
                await fixture.Workspace.SetDispositionAsync(fixture.Manager,
                    new(id, created.Item.Version, state, "F03 история архива"), "f03", CancellationToken.None);
                continue;
            }
            // Seed existing statuses, without introducing any new classification command.
            await using var db = fixture.Sandbox.Context();
            (await db.Listings.SingleAsync(item => item.Id == id)).Disposition = state;
            await db.SaveChangesAsync();
        }
        Guid foreign = await fixture.Workspace.CreateManualAsync(fixture.ForeignOwner,
            new(CatalogSource.Telegram, "Чужой архив", "Химки", 1m, 100m,
                null, null, null, null, "F03 изоляция"), "f03", CancellationToken.None);
        await using (var db = fixture.Sandbox.Context())
        {
            (await db.Listings.SingleAsync(item => item.Id == foreign)).Disposition = CatalogDisposition.Sold;
            await db.SaveChangesAsync();
        }
        var saved = await presets.CreateAsync(fixture.Manager, new(null, "Только входящие", Criteria() with { MaxTotalPrice = 1m }), CancellationToken.None);
        var request = new IncomingCatalogReadFilter(new(Source: CatalogSource.Avito, MaxPrice: 1m,
            MinAreaSquareMeters: 900m, AttentionOnly: true, Size: 2), Preset: IncomingCatalogPreset.New,
            MinPricePerSotka: 999999m, LandTypes: [IncomingLandType.Izhs],
            WorkingScope: new(IncomingCatalogMode.Archive, saved.Id, UseDraft: true, Slice: IncomingCatalogPreset.New));
        List<Guid> archiveIds = [];
        for (int offset = 0; offset < 5; offset += 2)
        {
            var page = await reads.ReadAsync(fixture.Manager, request with { Base = request.Base with { Offset = offset } }, CancellationToken.None);
            Assert.AreEqual(5, page.Total);
            Assert.IsNull(page.FilterCounts);
            archiveIds.AddRange(page.Items.Select(item => item.Id));
        }
        CollectionAssert.AreEquivalent(IncomingCatalogArchive.States.Select(state => ids[state]).ToArray(), archiveIds.ToArray());
        foreach (var state in IncomingCatalogArchive.States)
        {
            var page = await reads.ReadAsync(fixture.Manager, request with
                { WorkingScope = new(IncomingCatalogMode.Archive, ArchiveState: state) }, CancellationToken.None);
            Assert.AreEqual(1, page.Total);
            Assert.AreEqual(ids[state], page.Items.Single().Id);
        }
        var textPage = await reads.ReadAsync(fixture.Manager, request with { Base = new(Text: "Архив Sold") }, CancellationToken.None);
        Assert.AreEqual(ids[CatalogDisposition.Sold], textPage.Items.Single().Id);
        Assert.AreEqual(1L, (await presets.ReadAsync(fixture.Manager, CancellationToken.None)).Single().Version);
        Assert.AreEqual(1m, (await presets.ReadAsync(fixture.Manager, CancellationToken.None)).Single().Criteria.MaxTotalPrice);
        var detail = await reads.ReadDetailAsync(fixture.Manager, ids[CatalogDisposition.Dismissed], CancellationToken.None);
        Assert.AreEqual(CatalogDisposition.Dismissed, detail.Detail.Item.Disposition);
        Assert.IsTrue(detail.Detail.Events.Any(item => item.Kind == CatalogEventKind.Classified
            && item.Message.Contains("F03 история архива", StringComparison.Ordinal)));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => reads.ReadDetailAsync(fixture.ForeignOwner,
            ids[CatalogDisposition.Dismissed], CancellationToken.None));
        Assert.AreEqual(foreign, (await reads.ReadAsync(fixture.ForeignOwner, request, CancellationToken.None)).Items.Single().Id);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => reads.ReadAsync(fixture.Manager, request with
            { WorkingScope = new(IncomingCatalogMode.Archive, ArchiveState: CatalogDisposition.Incoming) }, CancellationToken.None));

        var pair = await fixture.IngestMarketplacePairAsync();
        Guid group = await pair.Administration.CreateGroupAsync(fixture.Owner, "Архивная группа", 10, "f03", CancellationToken.None);
        await using (var db = fixture.Sandbox.Context())
        {
            (await db.SearchConfigurations.SingleAsync(item => item.Source == CatalogSource.Avito)).SearchGroupId = group;
            (await db.Listings.SingleAsync(item => item.Id == pair.AvitoId)).Disposition = CatalogDisposition.RemovedAtSource;
            await db.SaveChangesAsync();
        }
        var grouped = await reads.ReadAsync(fixture.Manager, request with { SearchGroupId = group }, CancellationToken.None);
        Assert.AreEqual(1, grouped.Total);
        Assert.AreEqual(pair.AvitoId, grouped.Items.Single().Id);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => reads.ReadAsync(fixture.ForeignOwner,
            request with { SearchGroupId = group }, CancellationToken.None));
        await fixture.SetExplicitAccessAsync(fixture.ManagerEmployeeId, EmployeeAccessRules.NoAccess with { IncomingAccess = IncomingAccessLevel.Read });
        Assert.AreEqual(1, (await reads.ReadAsync(fixture.Manager, request with { SearchGroupId = group }, CancellationToken.None)).Total);
        await fixture.SetExplicitAccessAsync(fixture.ManagerEmployeeId, EmployeeAccessRules.NoAccess);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => reads.ReadAsync(fixture.Manager, request, CancellationToken.None));
    }
}
