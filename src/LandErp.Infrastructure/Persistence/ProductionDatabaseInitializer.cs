using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text.RegularExpressions;

namespace LandErp.Infrastructure.Persistence;

/// <summary>Explicit operator command for a single-host production database; never called by Server or Worker startup.</summary>
public static partial class ProductionDatabaseInitializer
{
    public static async Task InitializeAsync(string provisionerConnection, string migratorConnection,
        string runtimeConnection, CancellationToken cancellationToken = default)
    {
        NpgsqlConnectionStringBuilder provisioner = Parse(provisionerConnection);
        NpgsqlConnectionStringBuilder migrator = Parse(migratorConnection);
        NpgsqlConnectionStringBuilder runtime = Parse(runtimeConnection);
        ValidateBoundary(provisioner, migrator, runtime);

        await using (NpgsqlConnection admin = new(provisioner.ConnectionString))
        {
            await admin.OpenAsync(cancellationToken);
            await RequirePostgres18Async(admin, cancellationToken);
            await EnsureRoleAsync(admin, migrator.Username!, migrator.Password!, cancellationToken);
            await EnsureRoleAsync(admin, runtime.Username!, runtime.Password!, cancellationToken);
            await EnsureDatabaseAsync(admin, migrator.Database!, migrator.Username!, cancellationToken);
        }

        DbContextOptionsBuilder<LandErpDbContext> options = new();
        LandErpDbContext.Configure(options, migrator.ConnectionString);
        await using (LandErpDbContext db = new(options.Options))
        {
            await db.Database.MigrateAsync(cancellationToken);
        }

        await ApplyRuntimeAccessAsync(migrator.ConnectionString, runtime.Username!, cancellationToken);
        await VerifyRuntimeAsync(runtime.ConnectionString, cancellationToken);
    }

    public static async Task ApplyRuntimeAccessAsync(string migratorConnection, string runtimeRole,
        CancellationToken cancellationToken = default)
    {
        NpgsqlConnectionStringBuilder migrator = Parse(migratorConnection);
        ValidateName(runtimeRole, "runtime role");
        string role = QuoteIdentifier(runtimeRole);
        string database = QuoteIdentifier(migrator.Database!);
        string schemas = "foundation,identity,organization,collection,catalog,workflow,procurement";
        string sql = $"""
            REVOKE ALL ON DATABASE {database} FROM PUBLIC;
            REVOKE CREATE ON SCHEMA public FROM PUBLIC;
            REVOKE ALL ON SCHEMA foundation,identity,organization,collection,catalog,workflow,procurement FROM PUBLIC;
            GRANT CONNECT ON DATABASE {database} TO {role};
            GRANT USAGE ON SCHEMA foundation,identity,organization,collection,catalog,workflow,procurement TO {role};
            REVOKE ALL PRIVILEGES ON ALL TABLES IN SCHEMA {schemas} FROM {role};
            REVOKE ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA {schemas} FROM {role};
            GRANT SELECT ON ALL TABLES IN SCHEMA foundation TO {role};
            GRANT INSERT ON foundation.audit_events TO {role};
            GRANT SELECT,INSERT,UPDATE ON ALL TABLES IN SCHEMA identity,organization TO {role};
            GRANT DELETE ON identity.user_roles TO {role};
            GRANT SELECT,INSERT,UPDATE ON collection.agents,collection.search_groups,collection.search_group_market_settings,collection.search_configurations,collection.jobs,collection.scheduler_status,catalog.listings,catalog.incoming_filter_presets TO {role};
            GRANT SELECT,INSERT,UPDATE ON catalog.duplicate_candidates,catalog.duplicate_settings TO {role};
            GRANT SELECT,INSERT,UPDATE,DELETE ON catalog.object_groups TO {role};
            GRANT SELECT,INSERT,UPDATE,DELETE ON catalog.photo_fingerprints TO {role};
            GRANT SELECT,INSERT ON collection.deliveries,catalog.observations,catalog.events TO {role};
            GRANT SELECT ON workflow.stages TO {role};
            GRANT SELECT,INSERT,UPDATE ON workflow.assignments,workflow.work_tasks,procurement.property_cases,procurement.property_case_source_links,procurement.case_checks,procurement.case_check_template_items,procurement.case_document_requirements,procurement.inspection_template_items,procurement.site_inspections,procurement.site_inspection_items,foundation.notifications,foundation.stored_files TO {role};
            GRANT SELECT,INSERT ON workflow.transitions,workflow.approvals,foundation.business_timeline,procurement.negotiations,procurement.case_attachments,procurement.case_fact_revisions TO {role};
            GRANT USAGE ON ALL SEQUENCES IN SCHEMA procurement,identity,organization TO {role};
            """;
        await using NpgsqlConnection connection = new(migrator.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static NpgsqlConnectionStringBuilder Parse(string value)
    {
        PersistenceServices.ValidateConnection(value);
        NpgsqlConnectionStringBuilder result = new(value)
        {
            IncludeErrorDetail = false,
            LogParameters = false,
            Pooling = false
        };
        if (string.IsNullOrWhiteSpace(result.Password))
            throw new InvalidOperationException("Production database connections must use password authentication.");
        return result;
    }

    private static void ValidateBoundary(NpgsqlConnectionStringBuilder provisioner,
        NpgsqlConnectionStringBuilder migrator, NpgsqlConnectionStringBuilder runtime)
    {
        if (!IsLoopback(provisioner.Host) || !SameEndpoint(provisioner, migrator) || !SameEndpoint(migrator, runtime))
            throw new InvalidOperationException("Production database initialization is restricted to one PostgreSQL instance on loopback.");
        if (!string.Equals(migrator.Database, runtime.Database, StringComparison.Ordinal)
            || string.Equals(provisioner.Database, migrator.Database, StringComparison.Ordinal))
            throw new InvalidOperationException("Provisioner must use a maintenance database; migrator and runtime must target the same application database.");
        if (string.Equals(migrator.Username, runtime.Username, StringComparison.Ordinal))
            throw new InvalidOperationException("Migrator and runtime must use different roles.");
        ValidateName(migrator.Database!, "database");
        ValidateName(migrator.Username!, "migrator role");
        ValidateName(runtime.Username!, "runtime role");
    }

    private static bool IsLoopback(string? host) => host is not null
        && (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host is "127.0.0.1" or "::1");

    private static bool SameEndpoint(NpgsqlConnectionStringBuilder left, NpgsqlConnectionStringBuilder right) =>
        IsLoopback(right.Host) && left.Port == right.Port;

    private static void ValidateName(string value, string label)
    {
        if (!SafeName().IsMatch(value)) throw new InvalidOperationException($"Production {label} name is invalid.");
    }

    private static async Task RequirePostgres18Async(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = new("SELECT current_setting('server_version_num')::integer", connection);
        if (await command.ExecuteScalarAsync(cancellationToken) is not int version || version < 180000 || version >= 190000)
            throw new InvalidOperationException("Production setup requires PostgreSQL 18.");
    }

    private static async Task EnsureRoleAsync(NpgsqlConnection connection, string role, string password,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand check = new(
            "SELECT rolcanlogin AND NOT rolsuper AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolinherit FROM pg_roles WHERE rolname=@role",
            connection);
        check.Parameters.AddWithValue("role", role);
        object? existing = await check.ExecuteScalarAsync(cancellationToken);
        if (existing is bool valid)
        {
            if (!valid) throw new InvalidOperationException("An existing production role has unsafe attributes.");
            return;
        }

        string sql = await FormatSqlAsync(connection,
            "SELECT format('CREATE ROLE %I LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT', @name, @password)",
            role, password, cancellationToken);
        await using NpgsqlCommand create = new(sql, connection);
        await create.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureDatabaseAsync(NpgsqlConnection connection, string database, string owner,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand check = new(
            "SELECT owner.rolname FROM pg_database db JOIN pg_roles owner ON owner.oid=db.datdba WHERE db.datname=@database",
            connection);
        check.Parameters.AddWithValue("database", database);
        string? existingOwner = await check.ExecuteScalarAsync(cancellationToken) as string;
        if (existingOwner != null)
        {
            if (!string.Equals(existingOwner, owner, StringComparison.Ordinal))
                throw new InvalidOperationException("The existing application database is not owned by the configured migrator role.");
            return;
        }

        string sql = await FormatSqlAsync(connection,
            "SELECT format('CREATE DATABASE %I OWNER %I', @name, @owner)", database, owner, cancellationToken);
        await using NpgsqlCommand create = new(sql, connection);
        await create.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<string> FormatSqlAsync(NpgsqlConnection connection, string format,
        string name, string value, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = new(format, connection);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue(format.Contains("@password", StringComparison.Ordinal) ? "password" : "owner", value);
        return (string)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("PostgreSQL did not format the provisioning command."));
    }

    private static async Task VerifyRuntimeAsync(string runtimeConnection, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = new(runtimeConnection);
        await connection.OpenAsync(cancellationToken);
        await using (NpgsqlCommand ready = new("SELECT count(*) > 0 FROM foundation.migration_history", connection))
        {
            if (await ready.ExecuteScalarAsync(cancellationToken) is not true)
                throw new InvalidOperationException("Runtime role cannot read the migrated schema.");
        }

        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using NpgsqlCommand forbidden = new("CREATE SCHEMA landerp_runtime_permission_probe", connection, transaction);
            await forbidden.ExecuteNonQueryAsync(cancellationToken);
            throw new InvalidOperationException("Runtime role unexpectedly has DDL permission.");
        }
        catch (PostgresException exception) when (exception.SqlState == "42501")
        {
            // Expected: runtime may use application tables but cannot create schema objects.
        }
        finally
        {
            await transaction.RollbackAsync(cancellationToken);
        }
    }

    private static string QuoteIdentifier(string value) => new NpgsqlCommandBuilder().QuoteIdentifier(value);

    [GeneratedRegex("^[a-z][a-z0-9_]{2,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeName();
}
