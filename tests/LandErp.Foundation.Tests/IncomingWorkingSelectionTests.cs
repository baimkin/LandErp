using System.Text.Json;
using System.Text.Json.Serialization;
using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Infrastructure.Modules.Catalog;
using LandErp.Infrastructure.Modules.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using NpgsqlTypes;

namespace LandErp.Foundation.Tests;

[TestClass]
[TestCategory("PostgreSQL")]
public sealed class IncomingWorkingSelectionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        { Converters = { new JsonStringEnumConverter() } };
    private static IncomingFilterPresetCriteriaV1 Criteria(decimal? min = null, decimal? max = null, Guid? group = null)
        => new(2, null, null, CatalogDisposition.Incoming, CatalogAgeRange.Any, min, max, null, null,
            null, null, [], false, IncomingCatalogSortField.ChangedAt, IncomingCatalogSortDirection.Descending,
            SearchGroupId: group);

    private static IncomingCatalogReadFilter Request(IncomingCatalogMode mode = IncomingCatalogMode.SavedFilters,
        Guid? selected = null, IncomingCatalogPreset? slice = null, Guid? group = null, int offset = 0, int size = 40)
        => new(new(Offset: offset, Size: size), SearchGroupId: group, WorkingScope: new(mode, selected, Slice: slice));

    [TestMethod]
    public async Task UnionAlternativesNewAndPaginationUseUniqueOrganizationResults()
    {
        await using var fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        await using var source = NpgsqlDataSource.Create(fixture.Sandbox.RuntimeConnection);
        IncomingFilterPresetService presets = new(source, new EmployeeAccessService(fixture.Factory));
        IncomingCatalogReadService reads = new(fixture.Factory, new EmployeeAccessService(fixture.Factory), fixture.Workspace, TimeProvider.System, presets);
        List<Guid> ids = [];
        foreach (int price in new[] { 1, 2, 3, 8 })
            ids.Add(await fixture.Workspace.CreateManualAsync(fixture.Manager,
                new(CatalogSource.Telegram, $"Участок {price}", "Химки", price * 1_000_000m, 1200m,
                    null, null, null, null, "F02 fixture"), "f02", CancellationToken.None));
        await fixture.Workspace.CreateManualAsync(fixture.ForeignOwner,
            new(CatalogSource.Telegram, "Чужая организация", "Химки", 1_000_000m, 1200m,
                null, null, null, null, "F02 foreign"), "f02", CancellationToken.None);
        var low = await presets.CreateAsync(fixture.Manager, new(null, "До 3 млн", Criteria(max: 3_000_000m)), CancellationToken.None);
        var overlap = await presets.CreateAsync(fixture.Manager, new(null, "От 2 до 5 млн", Criteria(2_000_000m, 5_000_000m)), CancellationToken.None);
        var page = await reads.ReadAsync(fixture.Manager, Request(size: 1), CancellationToken.None);
        Assert.AreEqual(3, page.Total);
        Assert.HasCount(1, page.Items);
        Assert.AreEqual(3, page.Summary.New);
        Assert.AreEqual(3, page.FilterCounts!.UngroupedCount);
        Assert.AreEqual(3, page.FilterCounts.AllPresetsCount);
        Assert.AreEqual(3, page.FilterCounts.PresetCounts[low.Id]);
        Assert.AreEqual(2, page.FilterCounts.PresetCounts[overlap.Id]);
        var next = await reads.ReadAsync(fixture.Manager, Request(offset: 1, size: 1), CancellationToken.None);
        Assert.AreEqual(page.Total, next.Total);
        Assert.AreNotEqual(page.Items.Single().Id, next.Items.Single().Id);

        await fixture.Workspace.RegisterViewAsync(fixture.Manager, ids[0], "f02-view", CancellationToken.None);
        page = await reads.ReadAsync(fixture.Manager, Request(selected: low.Id, slice: IncomingCatalogPreset.New), CancellationToken.None);
        Assert.AreEqual(2, page.Total);
        Assert.AreEqual(2, page.Summary.New);
        Assert.AreEqual(2, page.FilterCounts!.PresetCounts[overlap.Id]);
        var alternative = await reads.ReadAsync(fixture.Manager, Request(selected: overlap.Id, slice: IncomingCatalogPreset.New), CancellationToken.None);
        Assert.AreEqual(page.FilterCounts.PresetCounts[overlap.Id], alternative.Total);

        // A local draft may loosen the selected preset. It is not intersected with its stored copy.
        var draft = Request(selected: low.Id) with { Base = new(MaxPrice: 9_000_000m),
            WorkingScope = new(SelectedPresetId: low.Id, UseDraft: true) };
        Assert.AreEqual(4, (await reads.ReadAsync(fixture.Manager, draft, CancellationToken.None)).Total);
        Assert.AreEqual(3_000_000m, (await presets.ReadAsync(fixture.Manager, CancellationToken.None)).Single(item => item.Id == low.Id).Criteria.MaxTotalPrice);
        Assert.AreEqual(1L, (await presets.ReadAsync(fixture.Manager, CancellationToken.None)).Single(item => item.Id == low.Id).Version);

        var all = await reads.ReadAsync(fixture.Manager, Request(IncomingCatalogMode.AllListings, low.Id), CancellationToken.None);
        Assert.AreEqual(4, all.Total, "All mode ignores selected saved preset.");
        Assert.AreEqual(3, all.Summary.New);
        var allNew = await reads.ReadAsync(fixture.Manager, Request(IncomingCatalogMode.AllListings, slice: IncomingCatalogPreset.New), CancellationToken.None);
        Assert.AreEqual(all.Summary.New, allNew.Total);
        Assert.AreEqual(0, (await reads.ReadAsync(fixture.ForeignOwner, Request(), CancellationToken.None)).Total);
        Assert.AreEqual(0, (await reads.ReadAsync(fixture.ForeignOwner, Request(selected: low.Id), CancellationToken.None)).Total);

        // Each tile must count what its click opens, regardless of the currently active New slice.
        foreach (var slice in Enum.GetValues<IncomingCatalogPreset>())
        {
            int expected = slice switch
            {
                IncomingCatalogPreset.New => allNew.Summary.New,
                IncomingCatalogPreset.PriceChanged => allNew.Summary.PriceChanged,
                IncomingCatalogPreset.PossibleDuplicate => allNew.Summary.PossibleDuplicate,
                IncomingCatalogPreset.Incomplete => allNew.Summary.Incomplete,
                IncomingCatalogPreset.ReturnedFromMonitoring => allNew.Summary.ReturnedFromMonitoring,
                _ => allNew.Summary.ProcessedToday
            };
            Assert.AreEqual(expected, (await reads.ReadAsync(fixture.Manager,
                Request(IncomingCatalogMode.AllListings, slice: slice), CancellationToken.None)).Total);
        }
    }

    [TestMethod]
    public async Task GroupHeaderIsUnionAndSelectingAlternativeReplacesPreviousGroup()
    {
        await using var fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        await using var source = NpgsqlDataSource.Create(fixture.Sandbox.RuntimeConnection);
        IncomingFilterPresetService presets = new(source, new EmployeeAccessService(fixture.Factory));
        IncomingCatalogReadService reads = new(fixture.Factory, new EmployeeAccessService(fixture.Factory), fixture.Workspace, TimeProvider.System, presets);
        var pair = await fixture.IngestMarketplacePairAsync();
        Guid a = await pair.Administration.CreateGroupAsync(fixture.Owner, "Группа А", 10, "f02", CancellationToken.None);
        Guid b = await pair.Administration.CreateGroupAsync(fixture.Owner, "Группа Б", 20, "f02", CancellationToken.None);
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            foreach (var search in await db.SearchConfigurations.ToArrayAsync())
                search.SearchGroupId = search.Source == CatalogSource.Avito ? a : b;
            await db.SaveChangesAsync();
        }
        var first = await presets.CreateAsync(fixture.Manager, new(a, "А первый", Criteria(group: a)), CancellationToken.None);
        var second = await presets.CreateAsync(fixture.Manager, new(a, "А второй", Criteria(max: 5_000_000m, group: a)), CancellationToken.None);
        var third = await presets.CreateAsync(fixture.Manager, new(b, "Б первый", Criteria(group: b)), CancellationToken.None);
        var union = await reads.ReadAsync(fixture.Manager, Request(), CancellationToken.None);
        Assert.AreEqual(2, union.Total);
        Assert.AreEqual(1, union.FilterCounts!.GroupCounts[a]);
        Assert.AreEqual(1, union.FilterCounts.PresetCounts[first.Id]);
        Assert.AreEqual(1, union.FilterCounts.PresetCounts[second.Id]);
        var grouped = await reads.ReadAsync(fixture.Manager, Request(group: a), CancellationToken.None);
        Assert.AreEqual(1, grouped.Total);
        Assert.AreEqual(pair.AvitoId, grouped.Items.Single().Id);
        Assert.AreEqual(1, grouped.FilterCounts!.PresetCounts[third.Id], "Alternatives must not intersect the previous group.");
        var selected = await reads.ReadAsync(fixture.Manager, Request(selected: third.Id, group: a), CancellationToken.None);
        Assert.AreEqual(grouped.FilterCounts.PresetCounts[third.Id], selected.Total);
        Assert.AreEqual(pair.CianId, selected.Items.Single().Id);
        Assert.AreEqual(1, (await reads.ReadAsync(fixture.Manager, Request(IncomingCatalogMode.AllListings, group: a), CancellationToken.None)).Total);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => reads.ReadAsync(fixture.ForeignOwner, Request(group: a), CancellationToken.None));
    }

    [TestMethod]
    public async Task EmptyAndIncompatiblePresetsNeverFallBackToAllAndStateRemainsIndependent()
    {
        await using var fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        await using var source = NpgsqlDataSource.Create(fixture.Sandbox.RuntimeConnection);
        IncomingFilterPresetService presets = new(source, new EmployeeAccessService(fixture.Factory));
        IncomingCatalogReadService reads = new(fixture.Factory, new EmployeeAccessService(fixture.Factory), fixture.Workspace, TimeProvider.System, presets);
        Guid id = await fixture.CreateUnlinkedManualAsync();
        var empty = await reads.ReadAsync(fixture.Manager, Request(), CancellationToken.None);
        Assert.AreEqual(0, empty.Total);
        Assert.AreEqual(0, empty.FilterCounts!.ApplicableCount);
        Assert.AreEqual(0, empty.Summary.New);
        Assert.AreEqual(1, (await reads.ReadAsync(fixture.Manager, Request(IncomingCatalogMode.AllListings), CancellationToken.None)).Total);

        var broken = await presets.CreateAsync(fixture.Manager, new(null, "Недоступный поиск", Criteria()), CancellationToken.None);
        var valid = await presets.CreateAsync(fixture.Manager, new(null, "Применимый", Criteria()), CancellationToken.None);
        await using (var connection = new NpgsqlConnection(fixture.Sandbox.MigratorConnection))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("UPDATE catalog.incoming_filter_presets SET criteria_json = @json WHERE id = @id", connection);
            command.Parameters.AddWithValue("id", broken.Id);
            command.Parameters.AddWithValue("json", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(Criteria() with
                { SchemaVersion = 1, SearchConfigurationId = Guid.NewGuid() }, JsonOptions));
            await command.ExecuteNonQueryAsync();
        }
        var partial = await reads.ReadAsync(fixture.Manager, Request(), CancellationToken.None);
        Assert.AreEqual(1, partial.Total);
        Assert.AreEqual(1, partial.Summary.Incomplete);
        Assert.AreEqual(partial.Summary.Incomplete, (await reads.ReadAsync(fixture.Manager,
            Request(slice: IncomingCatalogPreset.Incomplete), CancellationToken.None)).Total);
        Assert.IsNotNull(partial.FilterCounts!.Presets.Single(item => item.Id == broken.Id).CompatibilityIssue);
        Assert.AreEqual(1, partial.FilterCounts.ApplicableCount);
        Assert.AreEqual(0, (await reads.ReadAsync(fixture.Manager, Request(selected: broken.Id), CancellationToken.None)).Total);
        await presets.DeleteAsync(fixture.Manager, new(valid.Id, valid.Version), CancellationToken.None);
        Assert.AreEqual(0, (await reads.ReadAsync(fixture.Manager, Request(), CancellationToken.None)).Total);
        Assert.AreEqual(1, (await reads.ReadAsync(fixture.Manager, Request(IncomingCatalogMode.AllListings), CancellationToken.None)).Total);

        var detail = await fixture.Workspace.ReadItemAsync(fixture.Manager, id, CancellationToken.None);
        await fixture.Workspace.SetDispositionAsync(fixture.Manager,
            new(id, detail.Item.Version, CatalogDisposition.Fake, "F02 состояние отдельно"), "f02", CancellationToken.None);
        var all = await reads.ReadAsync(fixture.Manager, Request(IncomingCatalogMode.AllListings), CancellationToken.None);
        Assert.AreEqual(0, all.Total, "All stream still defaults to Incoming, not archive.");
        Assert.AreEqual(1, all.Summary.ProcessedToday);
        Assert.AreEqual(all.Summary.ProcessedToday, (await reads.ReadAsync(fixture.Manager,
            Request(IncomingCatalogMode.AllListings, slice: IncomingCatalogPreset.ProcessedToday), CancellationToken.None)).Total);
    }
}
