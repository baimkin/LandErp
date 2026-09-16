using System.Text.Json;
using System.Text.Json.Serialization;
using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace LandErp.Infrastructure.Modules.Catalog;

/// <summary>Organization-scoped saved Incoming filter states. Search group is optional organization metadata, not filter semantics.</summary>
public sealed class IncomingFilterPresetService(NpgsqlDataSource dataSource, IAccessControl access) : IIncomingFilterPresetService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<IReadOnlyList<IncomingFilterPresetView>> ReadAsync(Subject subject, CancellationToken cancellationToken)
    {
        AccessContext context = await access.RequireAsync(subject, Permissions.QueueRead, cancellationToken);
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, search_group_id, name, criteria_json::text, sort_order, version
            FROM catalog.incoming_filter_presets
            WHERE organization_id = @organization_id AND active
            ORDER BY search_group_id NULLS FIRST, sort_order, name, id
            """;
        command.Parameters.AddWithValue("organization_id", context.OrganizationId);
        List<IncomingFilterPresetView> result = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadView(reader));
        return result;
    }

    public async Task<IncomingFilterPresetView> CreateAsync(Subject subject, CreateIncomingFilterPreset command, CancellationToken cancellationToken)
    {
        AccessContext context = await access.RequireAsync(subject, Permissions.ManagerDecide, cancellationToken);
        string name = ValidateName(command.Name);
        ValidateCriteria(command.Criteria);
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        if (command.SearchGroupId != null)
            await EnsureGroupAsync(connection, context.OrganizationId, command.SearchGroupId.Value, cancellationToken);
        await EnsureSearchConfigurationAsync(connection, context.OrganizationId,
            command.Criteria.SearchConfigurationId, cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            int sortOrder;
            await using (NpgsqlCommand order = connection.CreateCommand())
            {
                order.Transaction = transaction;
                order.CommandText = """
                    SELECT COALESCE(MAX(sort_order), 0) + 10
                    FROM catalog.incoming_filter_presets
                    WHERE organization_id = @organization_id
                      AND search_group_id IS NOT DISTINCT FROM @search_group_id
                      AND active
                    """;
                order.Parameters.AddWithValue("organization_id", context.OrganizationId);
                AddNullableGuid(order.Parameters, "search_group_id", command.SearchGroupId);
                sortOrder = Convert.ToInt32(await order.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
            }

            Guid id = Guid.CreateVersion7();
            string criteriaJson = JsonSerializer.Serialize(command.Criteria, JsonOptions);
            await using NpgsqlCommand insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO catalog.incoming_filter_presets
                    (id, organization_id, search_group_id, name, criteria_json, sort_order, active, version)
                VALUES (@id, @organization_id, @search_group_id, @name, CAST(@criteria_json AS jsonb), @sort_order, TRUE, 1)
                """;
            insert.Parameters.AddWithValue("id", id);
            insert.Parameters.AddWithValue("organization_id", context.OrganizationId);
            AddNullableGuid(insert.Parameters, "search_group_id", command.SearchGroupId);
            insert.Parameters.AddWithValue("name", name);
            insert.Parameters.AddWithValue("criteria_json", criteriaJson);
            insert.Parameters.AddWithValue("sort_order", sortOrder);
            await insert.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(id, command.SearchGroupId, name, command.Criteria, sortOrder, 1);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new ArgumentException("Сохранённый фильтр с таким названием уже существует в выбранной группе.");
        }
    }

    public async Task<IncomingFilterPresetView> RenameAsync(Subject subject, RenameIncomingFilterPreset command, CancellationToken cancellationToken)
    {
        AccessContext context = await access.RequireAsync(subject, Permissions.ManagerDecide, cancellationToken);
        string name = ValidateName(command.Name);
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        try
        {
            await using NpgsqlCommand update = connection.CreateCommand();
            update.CommandText = """
                UPDATE catalog.incoming_filter_presets
                SET name = @name, version = version + 1
                WHERE id = @id AND organization_id = @organization_id AND active AND version = @expected_version
                """;
            update.Parameters.AddWithValue("name", name);
            update.Parameters.AddWithValue("id", command.Id);
            update.Parameters.AddWithValue("organization_id", context.OrganizationId);
            update.Parameters.AddWithValue("expected_version", command.ExpectedVersion);
            int changed = await update.ExecuteNonQueryAsync(cancellationToken);
            if (changed != 1) throw new DbUpdateConcurrencyException("Сохранённый фильтр изменился. Обновите страницу и повторите действие.");
            return await ReadOneAsync(connection, context.OrganizationId, command.Id, cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ArgumentException("Сохранённый фильтр с таким названием уже существует в выбранной группе.");
        }
    }

    public async Task DeleteAsync(Subject subject, DeleteIncomingFilterPreset command, CancellationToken cancellationToken)
    {
        AccessContext context = await access.RequireAsync(subject, Permissions.ManagerDecide, cancellationToken);
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand update = connection.CreateCommand();
        update.CommandText = """
            UPDATE catalog.incoming_filter_presets
            SET active = FALSE, version = version + 1
            WHERE id = @id AND organization_id = @organization_id AND active AND version = @expected_version
            """;
        update.Parameters.AddWithValue("id", command.Id);
        update.Parameters.AddWithValue("organization_id", context.OrganizationId);
        update.Parameters.AddWithValue("expected_version", command.ExpectedVersion);
        int changed = await update.ExecuteNonQueryAsync(cancellationToken);
        if (changed != 1) throw new DbUpdateConcurrencyException("Сохранённый фильтр изменился. Обновите страницу и повторите действие.");
    }

    private static IncomingFilterPresetView ReadView(NpgsqlDataReader reader)
    {
        string json = reader.GetString(3);
        IncomingFilterPresetCriteriaV1 criteria = JsonSerializer.Deserialize<IncomingFilterPresetCriteriaV1>(json, JsonOptions)
            ?? throw new InvalidOperationException("Сохранённый фильтр содержит пустые критерии.");
        ValidateCriteria(criteria);
        Guid? groupId = reader.IsDBNull(1) ? null : reader.GetGuid(1);
        return new(reader.GetGuid(0), groupId, reader.GetString(2), criteria, reader.GetInt32(4), reader.GetInt64(5));
    }

    private static async Task<IncomingFilterPresetView> ReadOneAsync(NpgsqlConnection connection, Guid organizationId, Guid id,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, search_group_id, name, criteria_json::text, sort_order, version
            FROM catalog.incoming_filter_presets
            WHERE id = @id AND organization_id = @organization_id AND active
            """;
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("organization_id", organizationId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new DbUpdateConcurrencyException("Сохранённый фильтр больше недоступен.");
        return ReadView(reader);
    }

    private static async Task EnsureGroupAsync(NpgsqlConnection connection, Guid organizationId, Guid searchGroupId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM collection.search_groups WHERE id = @id AND organization_id = @organization_id AND active)";
        command.Parameters.AddWithValue("id", searchGroupId);
        command.Parameters.AddWithValue("organization_id", organizationId);
        if (await command.ExecuteScalarAsync(cancellationToken) is not true)
            throw new ArgumentException("Группа поиска не найдена или недоступна.");
    }

    private static async Task EnsureSearchConfigurationAsync(NpgsqlConnection connection, Guid organizationId,
        Guid? searchConfigurationId, CancellationToken cancellationToken)
    {
        if (searchConfigurationId == null) return;
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1 FROM collection.search_configurations
                WHERE id = @id AND organization_id = @organization_id)
            """;
        command.Parameters.AddWithValue("id", searchConfigurationId.Value);
        command.Parameters.AddWithValue("organization_id", organizationId);
        if (await command.ExecuteScalarAsync(cancellationToken) is not true)
            throw new ArgumentException("Поисковая конфигурация не найдена или недоступна.");
    }

    private static void AddNullableGuid(NpgsqlParameterCollection parameters, string name, Guid? value)
    {
        NpgsqlParameter parameter = parameters.Add(name, NpgsqlDbType.Uuid);
        parameter.Value = value.HasValue ? (object)value.Value : DBNull.Value;
    }

    private static string ValidateName(string value)
    {
        string name = value.Trim();
        if (name.Length is < 2 or > 120) throw new ArgumentException("Название сохранённого фильтра должно содержать от 2 до 120 символов.");
        return name;
    }

    private static void ValidateCriteria(IncomingFilterPresetCriteriaV1 criteria)
    {
        IncomingLandType[] landTypes = criteria.LandTypes ?? [];
        if (criteria.SchemaVersion != 1 || !Enum.IsDefined(criteria.Age) || !Enum.IsDefined(criteria.SortField)
            || !Enum.IsDefined(criteria.SortDirection) || (criteria.Source != null && !Enum.IsDefined(criteria.Source.Value))
            || (criteria.Disposition != null && !Enum.IsDefined(criteria.Disposition.Value))
            || (criteria.Preset != null && !Enum.IsDefined(criteria.Preset.Value))
            || landTypes.Any(value => !Enum.IsDefined(value)) || landTypes.Length != landTypes.Distinct().Count()
            || criteria.MinTotalPrice < 0 || criteria.MaxTotalPrice < 0 || criteria.MinPricePerSotka < 0
            || criteria.MaxPricePerSotka < 0 || criteria.MinAreaSquareMeters < 0 || criteria.MaxAreaSquareMeters < 0
            || criteria.MinTotalPrice > criteria.MaxTotalPrice || criteria.MinPricePerSotka > criteria.MaxPricePerSotka
            || criteria.MinAreaSquareMeters > criteria.MaxAreaSquareMeters)
            throw new ArgumentException("Некорректные критерии сохранённого фильтра.");
    }
}
