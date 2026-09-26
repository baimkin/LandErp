using System.Text.Json;
using System.Text.Json.Serialization;
using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace LandErp.Infrastructure.Modules.Catalog;

/// <summary>Organization-shared filters. V2 stores the group predicate in criteria; V1 is resolved without read-side writes.</summary>
public sealed class IncomingFilterPresetService(
    NpgsqlDataSource dataSource,
    IEmployeeAccessService employeeAccess) : IIncomingFilterPresetService
{
    private async Task<AccessContext> RequireAsync(Subject subject, bool process, CancellationToken cancellationToken)
    {
        EffectiveEmployeeAccess effective = await employeeAccess.ResolveAsync(subject, cancellationToken);
        if (process ? !effective.CanProcessIncoming : !effective.CanReadIncoming) throw new AccessDeniedException();
        return effective.OrganizationContext;
    }
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<IReadOnlyList<IncomingFilterPresetView>> ReadAsync(Subject subject, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireAsync(subject, process: false, cancellationToken);
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
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) result.Add(ReadView(reader));
        for (int index = 0; index < result.Count; index++)
            result[index] = await ResolveAsync(connection, context.OrganizationId, result[index], cancellationToken);
        return result;
    }

    public async Task<IncomingFilterPresetView> CreateAsync(Subject subject, CreateIncomingFilterPreset command, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireAsync(subject, process: true, cancellationToken);
        string name = ValidateName(command.Name);
        ValidateCriteria(command.Criteria);
        RequireCurrentCriteria(command.Criteria);
        if (command.SearchGroupId != command.Criteria.SearchGroupId)
            throw new ArgumentException("Группа фильтра должна совпадать с групповым условием.");
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (command.SearchGroupId != null)
            await EnsureGroupAsync(connection, context.OrganizationId, command.SearchGroupId.Value, cancellationToken);
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

    public async Task<IncomingFilterPresetView> UpdateAsync(Subject subject, UpdateIncomingFilterPreset command,
        CancellationToken cancellationToken)
    {
        AccessContext context = await RequireAsync(subject, process: true, cancellationToken);
        string name = ValidateName(command.Name);
        ValidateCriteria(command.Criteria);
        RequireCurrentCriteria(command.Criteria);
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        IncomingFilterPresetView original = await ReadOneAsync(connection, context.OrganizationId, command.Id, cancellationToken);
        if (original.Version != command.ExpectedVersion)
            throw new DbUpdateConcurrencyException("Сохранённый фильтр изменился. Обновите страницу и повторите действие.");
        original = await ResolveAsync(connection, context.OrganizationId, original, cancellationToken);
        // An ambiguous legacy predicate must never be overwritten by a guessed group or an unbounded filter.
        if (original.CompatibilityIssue != null) throw new ArgumentException(original.CompatibilityIssue);
        if (command.Criteria.SearchGroupId is Guid groupId)
            await EnsureGroupAsync(connection, context.OrganizationId, groupId, cancellationToken);
        try
        {
            await using NpgsqlCommand update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE catalog.incoming_filter_presets
                SET name = @name, search_group_id = @group_id, criteria_json = @criteria,
                    version = version + 1
                WHERE id = @id AND organization_id = @organization_id AND active AND version = @version
                RETURNING id, search_group_id, name, criteria_json::text, sort_order, version
                """;
            update.Parameters.AddWithValue("id", command.Id);
            update.Parameters.AddWithValue("organization_id", context.OrganizationId);
            update.Parameters.AddWithValue("version", command.ExpectedVersion);
            update.Parameters.AddWithValue("name", name);
            AddNullableGuid(update.Parameters, "group_id", command.Criteria.SearchGroupId);
            update.Parameters.AddWithValue("criteria", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(command.Criteria, JsonOptions));
            IncomingFilterPresetView result;
            await using (NpgsqlDataReader reader = await update.ExecuteReaderAsync(cancellationToken))
            {
                if (!await reader.ReadAsync(cancellationToken))
                    throw new DbUpdateConcurrencyException("Сохранённый фильтр изменился. Обновите страницу и повторите действие.");
                result = ReadView(reader);
            }
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ArgumentException("Сохранённый фильтр с таким названием уже существует в выбранной группе.");
        }
    }

    public async Task<IncomingFilterPresetView> RenameAsync(Subject subject, RenameIncomingFilterPreset command, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireAsync(subject, process: true, cancellationToken);
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
        AccessContext context = await RequireAsync(subject, process: true, cancellationToken);
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
        command.CommandText = "SELECT id FROM collection.search_groups WHERE id = @id AND organization_id = @organization_id AND active FOR SHARE";
        command.Parameters.AddWithValue("id", searchGroupId);
        command.Parameters.AddWithValue("organization_id", organizationId);
        if (await command.ExecuteScalarAsync(cancellationToken) is not Guid)
            throw new ArgumentException("Группа поиска не найдена или недоступна.");
    }

    private static async Task<IncomingFilterPresetView> ResolveAsync(NpgsqlConnection connection, Guid organizationId,
        IncomingFilterPresetView saved, CancellationToken cancellationToken)
    {
        Guid? groupId = saved.Criteria.SearchGroupId;
        if (saved.Criteria.SchemaVersion == 1)
        {
            // Metadata alone was never a predicate in V1. Preserve unbounded filters as unbounded.
            groupId = null;
            if (saved.Criteria.SearchConfigurationId is Guid searchId)
            {
                await using NpgsqlCommand search = connection.CreateCommand();
                search.CommandText = """
                    SELECT g.id FROM collection.search_configurations s
                    JOIN collection.search_groups g ON g.id = s.search_group_id AND g.organization_id = s.organization_id
                    WHERE s.id = @id AND s.organization_id = @organization_id AND g.active
                    """;
                search.Parameters.AddWithValue("id", searchId);
                search.Parameters.AddWithValue("organization_id", organizationId);
                groupId = await search.ExecuteScalarAsync(cancellationToken) as Guid?;
                if (groupId == null || (saved.SearchGroupId != null && saved.SearchGroupId != groupId))
                    return saved with { CompatibilityIssue = $"Фильтр «{saved.Name}» ({saved.Id}): поиск {searchId}, группа-метаданные {saved.SearchGroupId?.ToString() ?? "не задана"}, группа поиска {groupId?.ToString() ?? "недоступна"}. Перенос неоднозначен. Владелец должен выбрать: восстановить связь поиска с группой, подтвердить конкретную группу или явно снять ограничение. До решения применение и изменение условий заблокированы." };
            }
        }
        if (groupId == null)
            return saved with { SearchGroupId = null, Criteria = saved.Criteria with { SchemaVersion = 2, SearchConfigurationId = null, SearchGroupId = null } };
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM collection.search_groups WHERE id = @id AND organization_id = @organization_id AND active)";
        command.Parameters.AddWithValue("id", groupId.Value);
        command.Parameters.AddWithValue("organization_id", organizationId);
        if (await command.ExecuteScalarAsync(cancellationToken) is not true)
            return saved with { CompatibilityIssue = $"Группа фильтра «{saved.Name}» ({groupId}) недоступна. Владелец должен восстановить группу либо явно согласовать замену условия. Применение и изменение заблокированы." };
        return saved with { SearchGroupId = groupId,
            Criteria = saved.Criteria with { SchemaVersion = 2, SearchConfigurationId = null, SearchGroupId = groupId } };
    }

    private static void RequireCurrentCriteria(IncomingFilterPresetCriteriaV1 criteria)
    {
        if (criteria.SchemaVersion != 2 || criteria.SearchConfigurationId != null)
            throw new ArgumentException("Сохраняйте фильтр с групповым условием версии 2, без отдельного поиска.");
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
        if (criteria.SchemaVersion is not (1 or 2) || (criteria.SchemaVersion == 2 && criteria.SearchConfigurationId != null)
            || !Enum.IsDefined(criteria.Age) || !Enum.IsDefined(criteria.SortField)
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
