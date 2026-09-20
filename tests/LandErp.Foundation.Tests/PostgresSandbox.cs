using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Diagnostics;
using System.Security.Cryptography;

namespace LandErp.Foundation.Tests;

/// <summary>Only exact database/role names created by this instance may be dropped.</summary>
internal sealed class PostgresSandbox : IAsyncDisposable
{
    private readonly string adminConnection;
    private readonly string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly List<string> createdDatabases = [];
    private readonly List<string> createdRoles = [];
    private readonly string suffix = Guid.NewGuid().ToString("N");

    private PostgresSandbox(string adminConnection)
    {
        this.adminConnection = adminConnection;
    }

    public string DatabaseName => "landerp_test_" + suffix;
    internal string AdminConnection => adminConnection;
    public string MigratorRole => "le_m_" + suffix;
    public string RuntimeRole => "le_r_" + suffix;
    public string MigratorConnection => Connection(DatabaseName, MigratorRole);
    public string RuntimeConnection => Connection(DatabaseName, RuntimeRole);

    public static async Task<PostgresSandbox> CreateAsync()
        => await CreateAsync(initialize: true);

    internal static async Task<PostgresSandbox> CreateProductionTargetAsync()
        => await CreateAsync(initialize: false);

    private static async Task<PostgresSandbox> CreateAsync(bool initialize)
    {
        string admin = Environment.GetEnvironmentVariable("LANDERP_TEST_ADMIN_CONNECTION") ?? "";
        if (string.IsNullOrWhiteSpace(admin))
        {
            throw new InvalidOperationException("LANDERP_TEST_ADMIN_CONNECTION is required for real PostgreSQL tests.");
        }

        NpgsqlConnectionStringBuilder settings = new(admin);
        if (settings.Host is not ("localhost" or "127.0.0.1" or "::1") || settings.IncludeErrorDetail || settings.LogParameters)
        {
            throw new InvalidOperationException("Tests require a local administrator connection without detail/parameter logging.");
        }

        settings.Pooling = false;
        PostgresSandbox sandbox = new(settings.ConnectionString);
        try
        {
            await sandbox.ExecuteAdminAsync("SELECT 1");
            if (initialize)
            {
                await sandbox.CreateRoleAsync(sandbox.MigratorRole);
                await sandbox.CreateRoleAsync(sandbox.RuntimeRole);
                await sandbox.CreateDatabaseAsync(sandbox.DatabaseName);
            }
            else
            {
                // Register exact disposable names before the production initializer creates any subset of them.
                sandbox.createdRoles.Add(sandbox.MigratorRole);
                sandbox.createdRoles.Add(sandbox.RuntimeRole);
                sandbox.createdDatabases.Add(sandbox.DatabaseName);
            }
            return sandbox;
        }
        catch
        {
            await sandbox.DisposeAsync();
            // Provider errors are deliberately not attached to credential-bearing setup failures.
            throw new InvalidOperationException("Isolated PostgreSQL setup failed. Check local admin connectivity and role/database creation privileges.");
        }
    }

    public LandErpDbContext Context(bool runtime = false)
    {
        DbContextOptionsBuilder<LandErpDbContext> options = new();
        LandErpDbContext.Configure(options, runtime ? RuntimeConnection : MigratorConnection);
        return new(options.Options);
    }

    public async Task GrantRuntimeAsync()
    {
        await ProductionDatabaseInitializer.ApplyRuntimeAccessAsync(MigratorConnection, RuntimeRole);
    }

    public async Task BackupRestoreAsync()
    {
        await using NpgsqlConnection source = new(MigratorConnection);
        await source.OpenAsync();
        await using NpgsqlCommand sourceHistory = new("SELECT count(*) FROM foundation.migration_history", source);
        long expected = (long)(await sourceHistory.ExecuteScalarAsync())!;
        await using NpgsqlCommand sourceRows = new("SELECT (SELECT count(*) FROM catalog.listings)+(SELECT count(*) FROM catalog.observations)+(SELECT count(*) FROM catalog.events)+(SELECT count(*) FROM foundation.audit_events)+(SELECT count(*) FROM foundation.business_timeline)+(SELECT count(*) FROM workflow.transitions)+(SELECT count(*) FROM workflow.approvals)+(SELECT count(*) FROM procurement.property_cases)+(SELECT count(*) FROM procurement.property_case_source_links)", source);
        long expectedRows = (long)(await sourceRows.ExecuteScalarAsync())!;
        string restored = DatabaseName + "_restore";
        await CreateDatabaseAsync(restored);
        string directory = Path.Combine(FoundationTests.RepositoryRoot(), "artifacts", "stage1", DatabaseName);
        Directory.CreateDirectory(directory);
        string backup = Path.Combine(directory, "backup.dump");
        await RunPgToolAsync("pg_dump.exe", MigratorConnection, ["--format=custom", "--file", backup]);
        await RunPgToolAsync("pg_restore.exe", Connection(restored, MigratorRole),
            ["--exit-on-error", "--dbname", restored, "--no-owner", "--no-privileges", backup]);
        await using NpgsqlConnection connection = new(Connection(restored, MigratorRole));
        await connection.OpenAsync();
        await using NpgsqlCommand command = new("SELECT count(*) FROM foundation.migration_history", connection);
        if (await command.ExecuteScalarAsync() is not long count || count != expected)
        {
            throw new InvalidOperationException("Restored schema history mismatch.");
        }
        await using NpgsqlCommand restoredRows = new("SELECT (SELECT count(*) FROM catalog.listings)+(SELECT count(*) FROM catalog.observations)+(SELECT count(*) FROM catalog.events)+(SELECT count(*) FROM foundation.audit_events)+(SELECT count(*) FROM foundation.business_timeline)+(SELECT count(*) FROM workflow.transitions)+(SELECT count(*) FROM workflow.approvals)+(SELECT count(*) FROM procurement.property_cases)+(SELECT count(*) FROM procurement.property_case_source_links)", connection);
        if ((long)(await restoredRows.ExecuteScalarAsync())! != expectedRows) throw new InvalidOperationException("Restored business rows mismatch.");
    }

    private static async Task RunPgToolAsync(string tool, string connection, string[] arguments)
    {
        NpgsqlConnectionStringBuilder settings = new(connection);
        string binaries = Environment.GetEnvironmentVariable("LANDERP_POSTGRES_BIN")
            ?? @"C:\Program Files\PostgreSQL\18\bin";
        ProcessStartInfo start = new(Path.Combine(binaries, tool))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["PGHOST"] = settings.Host;
        start.Environment["PGPORT"] = settings.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        start.Environment["PGUSER"] = settings.Username;
        start.Environment["PGDATABASE"] = settings.Database;
        start.Environment["PGPASSWORD"] = settings.Password;
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("PostgreSQL tool did not start.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await Task.WhenAll(output, error);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("PostgreSQL backup/restore tool failed; private tool output suppressed.");
        }
    }

    private async Task CreateRoleAsync(string role)
    {
        await ExecuteAdminAsync($"CREATE ROLE \"{role}\" LOGIN PASSWORD '{password}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT");
        createdRoles.Add(role);
    }

    private async Task CreateDatabaseAsync(string database)
    {
        await ExecuteAdminAsync($"CREATE DATABASE \"{database}\" OWNER \"{MigratorRole}\"");
        createdDatabases.Add(database);
        await ExecuteAdminAsync($"REVOKE ALL ON DATABASE \"{database}\" FROM PUBLIC");
    }

    private string Connection(string database, string role)
    {
        NpgsqlConnectionStringBuilder settings = new(adminConnection)
        {
            Database = database,
            Username = role,
            Password = password,
            Pooling = false,
            Timeout = 3,
            CommandTimeout = 5,
            IncludeErrorDetail = false,
            LogParameters = false
        };
        return settings.ConnectionString;
    }

    private async Task ExecuteAdminAsync(string sql, string? database = null)
    {
        NpgsqlConnectionStringBuilder settings = new(adminConnection);
        if (database != null)
        {
            settings.Database = database;
        }

        await using NpgsqlConnection connection = new(settings.ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (string database in createdDatabases.AsEnumerable().Reverse())
        {
            if (database != DatabaseName && database != DatabaseName + "_restore")
            {
                throw new InvalidOperationException("Refusing cleanup of an unowned database.");
            }

            await ExecuteAdminAsync($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)");
        }

        foreach (string role in createdRoles.AsEnumerable().Reverse())
        {
            if (role != MigratorRole && role != RuntimeRole)
            {
                throw new InvalidOperationException("Refusing cleanup of an unowned role.");
            }

            await ExecuteAdminAsync($"DROP ROLE IF EXISTS \"{role}\"");
        }
    }
}
