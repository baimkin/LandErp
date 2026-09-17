using LandErp.Infrastructure.Modules.IdentityAccess;
using LandErp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Security.Cryptography;
using System.Text.Json;

// Explicit Local tooling is a separate executable. Server/Worker never invoke it.
try
{
    string root = Environment.GetEnvironmentVariable("LANDERP_REPOSITORY_ROOT")
        ?? throw new InvalidOperationException("Repository root is required.");
    string directory = Path.Combine(root, "local-data", "stage1");
    string settingsFile = Path.Combine(directory, "settings.json");
    bool inspect = args is ["--inspect"];
    if (args.Length > 0 && !inspect || inspect && !File.Exists(settingsFile))
        throw new InvalidOperationException("Inspection requires an initialized Local database.");
    LocalSettings settings;
    if (File.Exists(settingsFile))
    {
        settings = JsonSerializer.Deserialize<LocalSettings>(await File.ReadAllTextAsync(settingsFile))
            ?? throw new InvalidOperationException("Invalid local settings.");
        NpgsqlConnectionStringBuilder existing = new(settings.MigratorConnection);
        if (existing.Database != "landerp_local" || existing.Host is not ("127.0.0.1" or "localhost" or "::1"))
            throw new InvalidOperationException("Local tooling cannot target this database.");
    }
    else
    {
        string admin = Environment.GetEnvironmentVariable("LANDERP_TEST_ADMIN_CONNECTION") ?? "";
        PersistenceServices.ValidateConnection(admin);
        NpgsqlConnectionStringBuilder adminSettings = new(admin);
        if (adminSettings.Host is not ("127.0.0.1" or "localhost" or "::1"))
            throw new InvalidOperationException("Local database must be on loopback.");
        await using NpgsqlConnection connection = new(admin);
        await connection.OpenAsync();
        await using NpgsqlCommand check = new("SELECT EXISTS(SELECT FROM pg_database WHERE datname = 'landerp_local')", connection);
        if (await check.ExecuteScalarAsync() is true)
            throw new InvalidOperationException("Existing landerp_local is not owned by this setup. Configure migrator/runtime credentials explicitly; no overwrite performed.");
        string suffix = Guid.NewGuid().ToString("N");
        string migrator = "le_local_m_" + suffix;
        string runtime = "le_local_r_" + suffix;
        string migrationPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        string runtimePassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using NpgsqlCommand roles = new($"CREATE ROLE \"{migrator}\" LOGIN PASSWORD '{migrationPassword}' NOSUPERUSER NOCREATEDB NOCREATEROLE; "
            + $"CREATE ROLE \"{runtime}\" LOGIN PASSWORD '{runtimePassword}' NOSUPERUSER NOCREATEDB NOCREATEROLE;", connection);
        await roles.ExecuteNonQueryAsync();
        await using NpgsqlCommand create = new($"CREATE DATABASE landerp_local OWNER \"{migrator}\"", connection);
        await create.ExecuteNonQueryAsync();
        settings = new(ForRole(adminSettings, migrator, migrationPassword), ForRole(adminSettings, runtime, runtimePassword));
        Directory.CreateDirectory(directory);
        // New file only; existing user files are never overwritten.
        await using FileStream stream = new(settingsFile, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, settings);
    }

    if (inspect)
    {
        await InspectAsync(settings.RuntimeConnection);
        return;
    }
    HostApplicationBuilder builder = Host.CreateApplicationBuilder([]);
    builder.Logging.ClearProviders();
    builder.Configuration["Database:ConnectionString"] = settings.MigratorConnection;
    builder.Services.AddLandErpPersistence(builder.Configuration);
    builder.Services.AddLandErpIdentity();
    using IHost host = builder.Build();
    await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
    LandErpDbContext db = scope.ServiceProvider.GetRequiredService<LandErpDbContext>();
    await db.Database.MigrateAsync();
    if (!await db.Users.AnyAsync())
    {
        string login = Environment.GetEnvironmentVariable("LANDERP_OWNER_LOGIN") ?? "owner@landerp.local";
        string password = Environment.GetEnvironmentVariable("LANDERP_OWNER_PASSWORD")
            ?? "Le!0" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        await LocalBootstrap.CreateOwnerAsync(db, scope.ServiceProvider.GetRequiredService<UserManager<LandErpUser>>(),
            scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>(), login, password, "LandErp");
        string ownerFile = Path.Combine(directory, "owner-access.txt");
        if (!File.Exists(ownerFile))
            await File.WriteAllTextAsync(ownerFile, "Login: " + login + Environment.NewLine + "Password: " + password);
    }
    NpgsqlConnectionStringBuilder runtimeSettings = new(settings.RuntimeConnection);
    string runtimeRole = runtimeSettings.Username ?? throw new InvalidOperationException("Runtime role missing.");
    if (!runtimeRole.StartsWith("le_local_r_", StringComparison.Ordinal) || !runtimeRole.All(character => char.IsAsciiLetterOrDigit(character) || character == '_'))
        throw new InvalidOperationException("Unexpected local runtime role.");
    await using NpgsqlConnection migratorConnection = new(settings.MigratorConnection);
    await migratorConnection.OpenAsync();
    await using NpgsqlCommand grants = new($"REVOKE CREATE ON SCHEMA public FROM PUBLIC; "
        + $"GRANT CONNECT ON DATABASE landerp_local TO \"{runtimeRole}\"; "
        + $"GRANT USAGE ON SCHEMA identity,organization,foundation TO \"{runtimeRole}\"; "
        + $"GRANT SELECT,INSERT,UPDATE ON ALL TABLES IN SCHEMA identity,organization TO \"{runtimeRole}\"; "
        + $"GRANT DELETE ON identity.user_roles TO \"{runtimeRole}\"; "
        + $"GRANT USAGE ON SCHEMA collection,catalog TO \"{runtimeRole}\"; "
        + $"GRANT SELECT,INSERT,UPDATE ON collection.agents,collection.search_groups,collection.search_group_market_settings,collection.search_configurations,collection.jobs,collection.scheduler_status,catalog.listings TO \"{runtimeRole}\"; "
        + $"GRANT SELECT,INSERT,UPDATE ON catalog.incoming_filter_presets TO \"{runtimeRole}\"; "
        + $"GRANT SELECT,INSERT ON collection.deliveries,catalog.observations,catalog.events TO \"{runtimeRole}\"; "
        + $"GRANT USAGE ON SCHEMA workflow,procurement TO \"{runtimeRole}\"; "
        + $"GRANT SELECT ON workflow.stages TO \"{runtimeRole}\"; "
        + $"GRANT SELECT,INSERT,UPDATE ON workflow.assignments,workflow.work_tasks,procurement.property_cases,procurement.property_case_source_links,procurement.case_checks,procurement.case_check_template_items,procurement.case_document_requirements,procurement.inspection_template_items,procurement.site_inspections,procurement.site_inspection_items,foundation.notifications,foundation.stored_files TO \"{runtimeRole}\"; "
        + $"GRANT SELECT,INSERT ON workflow.transitions,workflow.approvals,foundation.business_timeline,procurement.negotiations,procurement.case_attachments,procurement.case_fact_revisions TO \"{runtimeRole}\"; "
        + $"GRANT USAGE ON ALL SEQUENCES IN SCHEMA procurement TO \"{runtimeRole}\"; "
        + $"GRANT USAGE ON ALL SEQUENCES IN SCHEMA identity,organization TO \"{runtimeRole}\"; "
        + $"GRANT SELECT ON ALL TABLES IN SCHEMA foundation TO \"{runtimeRole}\"; "
        + $"GRANT INSERT ON foundation.audit_events TO \"{runtimeRole}\";", migratorConnection);
    await grants.ExecuteNonQueryAsync();
    Console.WriteLine("Local schema applied and Owner initialized. Credentials are in ignored local-data/stage1; values are not printed.");
}
catch
{
    Console.Error.WriteLine("Local setup failed. Check approved admin environment, local database ownership and permissions. Private exception suppressed.");
    Environment.ExitCode = 1;
}

static async Task InspectAsync(string runtimeConnection)
{
    PersistenceServices.ValidateConnection(runtimeConnection);
    NpgsqlConnectionStringBuilder target = new(runtimeConnection);
    if (target.Database != "landerp_local" || target.Host is not ("127.0.0.1" or "localhost" or "::1"))
        throw new InvalidOperationException("Only this Local database may be inspected.");
    await using NpgsqlConnection connection = new(runtimeConnection);
    await connection.OpenAsync();
    await using NpgsqlCommand metadata = new("""
        SELECT current_setting('server_version'),
            (SELECT count(*) FROM foundation.migration_history), count(*),
            count(obj_description(c.oid,'pg_class'))
        FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
        WHERE c.relkind='r' AND n.nspname IN
            ('foundation','identity','organization','collection','catalog','workflow','procurement')
        """, connection);
    await using (NpgsqlDataReader reader = await metadata.ExecuteReaderAsync())
    {
        await reader.ReadAsync();
        if (reader.GetInt64(2) != reader.GetInt64(3)) throw new InvalidOperationException("Missing table comments.");
        Console.WriteLine($"PostgreSQL {reader.GetString(0)}; migrations {reader.GetInt64(1)}; tables/comments {reader.GetInt64(2)}/{reader.GetInt64(3)}.");
    }
    // Even an unexpected grant cannot leave schema changes behind: probe always rolls back.
    await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
    try
    {
        await using NpgsqlCommand ddl = new("CREATE SCHEMA stage1_runtime_permission_probe", connection, transaction);
        try { await ddl.ExecuteNonQueryAsync(); throw new InvalidOperationException("Runtime DDL must be denied."); }
        catch (PostgresException exception) when (exception.SqlState == "42501")
        { Console.WriteLine("Runtime DDL denied (42501)."); }
    }
    finally { await transaction.RollbackAsync(); }
}

static string ForRole(NpgsqlConnectionStringBuilder admin, string role, string password)
{
    NpgsqlConnectionStringBuilder settings = new(admin.ConnectionString)
    { Database = "landerp_local", Username = role, Password = password, IncludeErrorDetail = false, LogParameters = false };
    return settings.ConnectionString;
}

internal sealed record LocalSettings(string MigratorConnection, string RuntimeConnection);
