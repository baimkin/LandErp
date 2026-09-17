using LandErp.Application.Foundation;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace LandErp.Foundation.Tests;

[TestClass]
[TestCategory("PostgreSQL")]
public sealed class PostgresTests
{
    [TestMethod]
    public async Task RealPostgresMigrationsCommentsRuntimeIsolationAndRecovery()
    {
        await using PostgresSandbox sandbox = await PostgresSandbox.CreateAsync();
        await using LandErpDbContext context = sandbox.Context();
        int expectedMigrations = context.Database.GetMigrations().Count();
        Assert.IsFalse(context.Database.HasPendingModelChanges());
        string sql = context.GetService<IMigrator>().GenerateScript();
        Assert.IsTrue(sql.Contains("COMMENT ON TABLE", StringComparison.Ordinal));
        Assert.IsFalse(sql.Contains("CREATE TABLE listing", StringComparison.OrdinalIgnoreCase));
        await using ServiceProvider readinessServices = IdentityOrganizationTests.Services(sandbox.MigratorConnection);
        IDatabaseStatus readiness = readinessServices.GetRequiredService<IDatabaseStatus>();
        Assert.IsFalse(await readiness.IsReadyAsync(CancellationToken.None));
        await context.GetService<IMigrator>().MigrateAsync("20260914160628_IdentityOrganization");
        Assert.IsFalse(await readiness.IsReadyAsync(CancellationToken.None), "Partial schema must not report ready.");
        await context.Database.MigrateAsync();
        await context.Database.MigrateAsync();
        Assert.AreEqual(expectedMigrations, (await context.Database.GetAppliedMigrationsAsync()).Count());
        Assert.AreEqual(0, (await context.Database.GetPendingMigrationsAsync()).Count());
        Assert.IsTrue(await readiness.IsReadyAsync(CancellationToken.None));

        await using NpgsqlConnection connection = new(sandbox.MigratorConnection);
        await connection.OpenAsync();
        await using NpgsqlCommand comments = new("""
            SELECT obj_description(c.oid, 'pg_class'),
                col_description(c.oid, a.attnum)
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
            WHERE n.nspname = 'foundation' AND c.relname = 'migration_history'
            """, connection);
        await using (NpgsqlDataReader reader = await comments.ExecuteReaderAsync())
        {
            int count = 0;
            while (await reader.ReadAsync())
            {
                Assert.IsTrue(reader.GetString(0).Any(character => character is >= 'А' and <= 'я'));
                Assert.IsTrue(reader.GetString(1).Any(character => character is >= 'А' and <= 'я'));
                count++;
            }

            Assert.AreEqual(2, count);
        }

        await using NpgsqlCommand missingTableComments = new("""
            SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
            WHERE n.nspname IN ('foundation','identity','organization','collection','catalog','workflow','procurement') AND c.relkind='r'
              AND (obj_description(c.oid,'pg_class') IS NULL OR obj_description(c.oid,'pg_class') !~ '[А-Яа-я]')
            """, connection);
        Assert.AreEqual(0L, await missingTableComments.ExecuteScalarAsync());
        foreach (var entity in context.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties().Where(item => item.GetComment() != null))
            {
                await using NpgsqlCommand metadata = new("""
                    SELECT col_description(c.oid,a.attnum) FROM pg_class c
                    JOIN pg_namespace n ON n.oid=c.relnamespace
                    JOIN pg_attribute a ON a.attrelid=c.oid
                    WHERE n.nspname=@schema AND c.relname=@table AND a.attname=@column
                    """, connection);
                metadata.Parameters.AddWithValue("schema", entity.GetSchema()!);
                metadata.Parameters.AddWithValue("table", entity.GetTableName()!);
                metadata.Parameters.AddWithValue("column", property.GetColumnName());
                Assert.AreEqual(property.GetComment(), await metadata.ExecuteScalarAsync());
            }
        }

        await sandbox.GrantRuntimeAsync();
        await using NpgsqlConnection runtime = new(sandbox.RuntimeConnection);
        await runtime.OpenAsync();
        foreach (string forbidden in new[]
        {
            "CREATE TABLE public.forbidden(id uuid)",
            "CREATE SCHEMA forbidden",
            "ALTER TABLE foundation.migration_history ADD COLUMN forbidden integer",
            "DELETE FROM foundation.migration_history"
        })
        {
            await using NpgsqlCommand command = new(forbidden, runtime);
            PostgresException error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.AreEqual(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
        }

        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:ConnectionString"] = sandbox.RuntimeConnection
        }).Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddLandErpPersistence(config);
        await using ServiceProvider provider = services.BuildServiceProvider();
        Assert.IsTrue(await provider.GetRequiredService<IDatabaseStatus>().IsReadyAsync(CancellationToken.None));
        await sandbox.BackupRestoreAsync();
        await context.GetService<IMigrator>().MigrateAsync("0");
        Assert.AreEqual(0, (await context.Database.GetAppliedMigrationsAsync()).Count());
        await context.Database.MigrateAsync();
        Assert.AreEqual(expectedMigrations, (await context.Database.GetAppliedMigrationsAsync()).Count());
    }

    [TestMethod]
    public async Task IndependentHostsHttpErrorsAndOutageReadiness()
    {
        await using PostgresSandbox sandbox = await PostgresSandbox.CreateAsync();
        await using LandErpDbContext context = sandbox.Context();
        await context.Database.MigrateAsync();
        await sandbox.GrantRuntimeAsync();
        using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(5) };
        int port = FreePort();
        using Process server = StartHost("LandErp.Server", sandbox.RuntimeConnection, port);
        try
        {
            await WaitForLiveAsync(server, http, port);
            Assert.AreEqual(HttpStatusCode.OK, (await http.GetAsync($"http://127.0.0.1:{port}/health/ready")).StatusCode);
            using HttpRequestMessage missing = new(HttpMethod.Get, $"http://127.0.0.1:{port}/unknown");
            missing.Headers.Add("X-Correlation-ID", "request-test_123");
            using HttpResponseMessage response = await http.SendAsync(missing);
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
            Assert.AreEqual("request-test_123", response.Headers.GetValues("X-Correlation-ID").Single());
            string problem = await response.Content.ReadAsStringAsync();
            Assert.IsTrue(problem.Contains("NOT_FOUND", StringComparison.Ordinal));
            Assert.IsFalse(problem.Contains("Password", StringComparison.OrdinalIgnoreCase));
            using HttpRequestMessage oversized = new(HttpMethod.Get, $"http://127.0.0.1:{port}/unknown");
            oversized.Headers.Add("X-Correlation-ID", new string('a', 65));
            using HttpResponseMessage rejected = await http.SendAsync(oversized);
            Assert.AreNotEqual(new string('a', 65), rejected.Headers.GetValues("X-Correlation-ID").Single());
            using Process worker = StartHost("LandErp.Worker", sandbox.RuntimeConnection);
            await Task.Delay(1000);
            Assert.IsFalse(worker.HasExited);
            worker.Kill(true);
            await worker.WaitForExitAsync();
            Assert.AreEqual(HttpStatusCode.OK, (await http.GetAsync($"http://127.0.0.1:{port}/health/ready")).StatusCode);
        }
        finally
        {
            await StopAsync(server);
        }

        NpgsqlConnectionStringBuilder outage = new(sandbox.RuntimeConnection) { Port = FreePort(), Timeout = 1 };
        using Process unavailable = StartHost("LandErp.Server", outage.ConnectionString, port);
        try
        {
            await WaitForLiveAsync(unavailable, http, port);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable,
                (await http.GetAsync($"http://127.0.0.1:{port}/health/ready")).StatusCode);
            Assert.AreEqual(HttpStatusCode.OK,
                (await http.GetAsync($"http://127.0.0.1:{port}/health/live")).StatusCode);
        }
        finally
        {
            await StopAsync(unavailable);
        }
    }

    internal static Process StartHost(string project, string connection, int? port = null, bool https = false)
    {
        string root = FoundationTests.RepositoryRoot();
        string executable = Environment.GetEnvironmentVariable("LANDERP_DOTNET")
            ?? Path.Combine(root, "artifacts", "stage1", "dotnet", "dotnet.exe");
        ProcessStartInfo start = new(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.Combine(root, "src", project)
        };
        string? testBuildRoot = Environment.GetEnvironmentVariable("LANDERP_TEST_BUILD_ROOT");
        string assembly = string.IsNullOrWhiteSpace(testBuildRoot)
            ? Path.Combine(root, "src", project, "bin", "Release", "net10.0", project + ".dll")
            : Path.Combine(testBuildRoot, "bin", project, "release", project + ".dll");
        start.ArgumentList.Add(assembly);
        start.Environment["Database__ConnectionString"] = connection;
        start.Environment["DOTNET_ENVIRONMENT"] = "Test";
        if (port.HasValue)
        {
            start.Environment["ASPNETCORE_URLS"] = $"{(https ? "https" : "http")}://127.0.0.1:{port}";
        }

        Process process = Process.Start(start) ?? throw new InvalidOperationException("Host failed to start.");
        // Drain output privately: credentials and internal exception details never enter test reports.
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static async Task WaitForLiveAsync(Process process, HttpClient http, int port)
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            if (process.HasExited)
            {
                Assert.Fail("Host exited before liveness; private process output suppressed.");
            }

            try
            {
                if ((await http.GetAsync($"http://127.0.0.1:{port}/health/live")).IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Startup is asynchronous; bounded polling is safe.
            }

            await Task.Delay(100);
        }

        Assert.Fail("Host liveness timeout.");
    }

    internal static async Task StopAsync(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(true);
            await process.WaitForExitAsync();
        }
    }

    internal static int FreePort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
