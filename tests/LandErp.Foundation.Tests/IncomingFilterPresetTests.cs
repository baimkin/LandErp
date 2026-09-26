using System.Text.Json;
using System.Text.Json.Serialization;
using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.Collection.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Infrastructure.Modules.Catalog;
using LandErp.Infrastructure.Modules.Collection;
using Microsoft.EntityFrameworkCore;
using LandErp.Infrastructure.Modules.IdentityAccess;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using NpgsqlTypes;

namespace LandErp.Foundation.Tests;

[TestClass]
[TestCategory("PostgreSQL")]
public sealed class IncomingFilterPresetTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static IncomingFilterPresetCriteriaV1 Criteria(Guid? group = null) => new(2, null, null,
        CatalogDisposition.Incoming, CatalogAgeRange.Any, null, 5_000_000m, null, null,
        null, null, [], false, IncomingCatalogSortField.ChangedAt, IncomingCatalogSortDirection.Descending,
        SearchGroupId: group);

    [TestMethod]
    public async Task UpdateCopyAndConflictingWritersPreserveIdentityAndOrganizationBoundary()
    {
        await using var fixture = await ProcurementTests.Phase1Fixture.CreateAsync(true, false);
        await using var dataSource = NpgsqlDataSource.Create(fixture.Sandbox.RuntimeConnection);
        IncomingFilterPresetService service = new(dataSource, new EmployeeAccessService(fixture.Factory));
        CollectionAdministration administration = new(fixture.Factory, TimeProvider.System);
        Guid group = await administration.CreateGroupAsync(fixture.Owner, "Группа F01", 10, "f01", CancellationToken.None);
        var initial = await service.CreateAsync(fixture.Manager, new(null, "Исходный", Criteria()), CancellationToken.None);
        var draft = initial.Criteria with { SearchGroupId = group, MaxTotalPrice = 3_000_000m };
        // Merely editing/applying a detached criterion cannot write the shared record.
        var unchanged = (await service.ReadAsync(fixture.SecondManager, CancellationToken.None)).Single();
        Assert.AreEqual(JsonSerializer.Serialize(initial.Criteria, JsonOptions), JsonSerializer.Serialize(unchanged.Criteria, JsonOptions));
        var updated = await service.UpdateAsync(fixture.Manager, new(initial.Id, initial.Version, "Обновлённый", draft), CancellationToken.None);
        Assert.AreEqual(initial.Id, updated.Id);
        Assert.AreEqual(initial.Version + 1, updated.Version);
        Assert.AreEqual(group, updated.SearchGroupId);
        Assert.AreEqual(3_000_000m, updated.Criteria.MaxTotalPrice);
        Assert.AreEqual("Обновлённый", updated.Name);
        await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(() => service.UpdateAsync(fixture.SecondManager,
            new(initial.Id, initial.Version, "Устаревшая запись", Criteria()), CancellationToken.None));
        var copy = await service.CreateAsync(fixture.Manager, new(group, "Копия", draft), CancellationToken.None);
        Assert.AreNotEqual(initial.Id, copy.Id);
        Assert.HasCount(2, await service.ReadAsync(fixture.SecondManager, CancellationToken.None));
        Assert.HasCount(0, await service.ReadAsync(fixture.ForeignOwner, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(() => service.UpdateAsync(fixture.ForeignOwner,
            new(updated.Id, updated.Version, "Чужая запись", Criteria()), CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.CreateAsync(fixture.ForeignOwner,
            new(group, "Чужая группа", draft), CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.UpdateAsync(fixture.Manager,
            new(updated.Id, updated.Version, "Нет группы", Criteria(Guid.NewGuid())), CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.CreateAsync(fixture.Manager,
            new(null, "Старый клиент", Criteria() with { SchemaVersion = 1 }), CancellationToken.None));

        // Two independently loaded editors race on the same version: exactly one can commit.
        async Task<bool> SaveAsync(string name)
        {
            try
            {
                await service.UpdateAsync(fixture.Manager, new(updated.Id, updated.Version, name, draft), CancellationToken.None);
                return true;
            }
            catch (DbUpdateConcurrencyException) { return false; }
        }
        bool[] outcomes = await Task.WhenAll(SaveAsync("Первый редактор"), SaveAsync("Второй редактор"));
        Assert.AreEqual(1, outcomes.Count(value => value));
        await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(() => service.DeleteAsync(fixture.Manager,
            new(updated.Id, updated.Version), CancellationToken.None));

        await fixture.SetExplicitAccessAsync(fixture.ManagerEmployeeId,
            EmployeeAccessRules.NoAccess with { IncomingAccess = IncomingAccessLevel.Read });
        Assert.HasCount(2, await service.ReadAsync(fixture.Manager, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => service.CreateAsync(fixture.Manager,
            new(null, "Нет права", Criteria()), CancellationToken.None));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => service.UpdateAsync(fixture.Manager,
            new(copy.Id, copy.Version, "Нет права", Criteria()), CancellationToken.None));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => service.DeleteAsync(fixture.Manager,
            new(copy.Id, copy.Version), CancellationToken.None));
    }

    [TestMethod]
    public async Task GroupPredicateIncludesLaterSearchAndExcludesOtherGroupsAndOrganizations()
    {
        await using var fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        await using var dataSource = NpgsqlDataSource.Create(fixture.Sandbox.RuntimeConnection);
        IncomingFilterPresetService service = new(dataSource, new EmployeeAccessService(fixture.Factory));
        var pair = await fixture.IngestMarketplacePairAsync();
        Guid group = await pair.Administration.CreateGroupAsync(fixture.Owner, "Общая группа", 10, "f01", CancellationToken.None);
        Guid other = await pair.Administration.CreateGroupAsync(fixture.Owner, "Другая группа", 20, "f01", CancellationToken.None);
        Guid foreign = await pair.Administration.CreateGroupAsync(fixture.ForeignOwner, "Чужая группа", 10, "f01", CancellationToken.None);
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            foreach (var search in await db.SearchConfigurations.ToArrayAsync())
                search.SearchGroupId = search.Source == CatalogSource.Avito ? group : other;
            await db.SaveChangesAsync();
        }
        var saved = await service.CreateAsync(fixture.Manager, new(group, "Динамическая группа", Criteria(group)), CancellationToken.None);
        IncomingCatalogReadService reads = new(fixture.Factory, fixture.Workspace, TimeProvider.System);
        var filter = new IncomingCatalogReadFilter(new(), SearchGroupId: saved.Criteria.SearchGroupId);
        var before = await reads.ReadAsync(fixture.Manager, filter, CancellationToken.None);
        CollectionAssert.AreEquivalent(new[] { pair.AvitoId }, before.Items.Select(item => item.Id).ToArray());

        // Add an actual new search after the preset was saved, then attach an observation to it.
        await pair.Administration.CreateSearchAsync(fixture.Owner,
            new("Поздний поиск", CatalogSource.Cian, "https://www.cian.ru/cat.php?deal_type=sale", 1, group), "f01", CancellationToken.None);
        await using (var db = fixture.Sandbox.Context())
        {
            Guid laterSearch = await db.SearchConfigurations.Where(item => item.Label == "Поздний поиск").Select(item => item.Id).SingleAsync();
            var observation = await db.ListingObservations.SingleAsync(item => item.ListingId == pair.CianId);
            var job = await db.CollectionJobs.SingleAsync(item => item.Id == observation.JobId);
            // Fixture reuses its received Cian observation as the later search result; production ingestion is outside F-01.
            job.SearchId = laterSearch;
            await db.SaveChangesAsync();
        }
        var after = await reads.ReadAsync(fixture.Manager, filter, CancellationToken.None);
        CollectionAssert.AreEquivalent(new[] { pair.AvitoId, pair.CianId }, after.Items.Select(item => item.Id).ToArray());
        Assert.AreEqual(saved.Version, (await service.ReadAsync(fixture.Manager, CancellationToken.None)).Single().Version);
        Assert.AreEqual(0, (await reads.ReadAsync(fixture.Manager, new(new(), SearchGroupId: other), CancellationToken.None)).Total);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => reads.ReadAsync(fixture.ForeignOwner, filter, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => reads.ReadAsync(fixture.Manager,
            new(new(), SearchGroupId: foreign), CancellationToken.None));
        await using (var db = fixture.Sandbox.Context())
        {
            (await db.SearchGroups.SingleAsync(item => item.Id == group)).Active = false;
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => reads.ReadAsync(fixture.Manager, filter, CancellationToken.None));
        Assert.IsNotNull((await service.ReadAsync(fixture.Manager, CancellationToken.None)).Single().CompatibilityIssue);
    }

    [TestMethod]
    public async Task LegacyResolutionPreservesConditionsAndBlocksAmbiguousConversionWithoutWrites()
    {
        await using var fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        await using var dataSource = NpgsqlDataSource.Create(fixture.Sandbox.RuntimeConnection);
        IncomingFilterPresetService service = new(dataSource, new EmployeeAccessService(fixture.Factory));
        var pair = await fixture.IngestMarketplacePairAsync();
        Guid group = await pair.Administration.CreateGroupAsync(fixture.Owner, "Группа поиска", 10, "f01", CancellationToken.None);
        Guid other = await pair.Administration.CreateGroupAsync(fixture.Owner, "Метаданные", 20, "f01", CancellationToken.None);
        Guid searchId;
        Guid ungroupedSearch;
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            var search = await db.SearchConfigurations.SingleAsync(item => item.Source == CatalogSource.Avito);
            search.SearchGroupId = group;
            searchId = search.Id;
            ungroupedSearch = await db.SearchConfigurations.Where(item => item.Source == CatalogSource.Cian).Select(item => item.Id).SingleAsync();
            await db.SaveChangesAsync();
        }
        var legacy = Criteria() with { SchemaVersion = 1, SearchConfigurationId = searchId, MinTotalPrice = 100m,
            LandTypes = [IncomingLandType.Izhs], Preset = IncomingCatalogPreset.PriceChanged };
        Guid good = await InsertLegacyAsync(fixture, "Однозначный", group, legacy);
        Guid noMetadata = await InsertLegacyAsync(fixture, "Без метаданных", null, legacy);
        Guid mismatch = await InsertLegacyAsync(fixture, "Противоречие", other, legacy);
        Guid missing = await InsertLegacyAsync(fixture, "Удалённый поиск", null, legacy with { SearchConfigurationId = Guid.NewGuid() });
        Guid noGroup = await InsertLegacyAsync(fixture, "Поиск без группы", null, legacy with { SearchConfigurationId = ungroupedSearch });
        Guid metadataOnly = await InsertLegacyAsync(fixture, "Только метаданные", other, legacy with { SearchConfigurationId = null });
        var records = (await service.ReadAsync(fixture.Manager, CancellationToken.None)).ToDictionary(item => item.Id);
        Assert.HasCount(6, records);
        foreach (Guid id in new[] { good, noMetadata })
        {
            var resolved = records[id];
            Assert.IsNull(resolved.CompatibilityIssue);
            Assert.AreEqual(group, resolved.Criteria.SearchGroupId);
            Assert.IsNull(resolved.Criteria.SearchConfigurationId);
            Assert.AreEqual(legacy.MinTotalPrice, resolved.Criteria.MinTotalPrice);
            Assert.AreEqual(legacy.Preset, resolved.Criteria.Preset);
            CollectionAssert.AreEqual(legacy.LandTypes, resolved.Criteria.LandTypes);
            Assert.AreEqual(1L, resolved.Version);
        }
        Assert.AreEqual("Однозначный", records[good].Name);
        Assert.IsNull(records[metadataOnly].Criteria.SearchGroupId);
        foreach (Guid id in new[] { mismatch, missing, noGroup })
        {
            Assert.IsNotNull(records[id].CompatibilityIssue);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.UpdateAsync(fixture.Manager,
                new(id, 1, "Не угадывать", Criteria()), CancellationToken.None));
        }
        await using (var connection = new NpgsqlConnection(fixture.Sandbox.MigratorConnection))
        {
            await connection.OpenAsync();
            await using var count = new NpgsqlCommand("SELECT count(*) FROM catalog.incoming_filter_presets WHERE criteria_json->>'schemaVersion' = '1' AND version = 1", connection);
            Assert.AreEqual(6L, await count.ExecuteScalarAsync());
        }
        var converted = await service.UpdateAsync(fixture.Manager,
            new(good, 1, records[good].Name, records[good].Criteria), CancellationToken.None);
        Assert.AreEqual(good, converted.Id);
        Assert.AreEqual(2, converted.Criteria.SchemaVersion);
        Assert.AreEqual(group, converted.Criteria.SearchGroupId);
    }

    private static async Task<Guid> InsertLegacyAsync(ProcurementTests.Phase1Fixture fixture, string name,
        Guid? metadataGroup, IncomingFilterPresetCriteriaV1 criteria)
    {
        Guid id = Guid.CreateVersion7();
        await using var connection = new NpgsqlConnection(fixture.Sandbox.MigratorConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO catalog.incoming_filter_presets
                (id, organization_id, search_group_id, name, criteria_json, sort_order, active, version)
            VALUES (@id, @org, @group, @name, @criteria, 10, TRUE, 1)
            """, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("org", fixture.OrganizationId);
        command.Parameters.AddWithValue("group", NpgsqlDbType.Uuid, (object?)metadataGroup ?? DBNull.Value);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("criteria", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(criteria, JsonOptions));
        await command.ExecuteNonQueryAsync();
        return id;
    }
}
