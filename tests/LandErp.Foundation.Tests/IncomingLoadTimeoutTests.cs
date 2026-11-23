using System.Data.Common;
using LandErp.Collector.Contracts.V1;
using LandErp.Infrastructure.Modules.Collection;
using System.Diagnostics;
using System.Text.Json;
using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.Collection.Domain;
using LandErp.Infrastructure.Modules.Catalog;
using LandErp.Infrastructure.Modules.IdentityAccess;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace LandErp.Foundation.Tests;

[TestClass]
[TestCategory("PostgreSQL")]
[TestCategory("Diagnostic")]
public sealed class IncomingLoadTimeoutTests
{
    [TestMethod]
    [DataRow(25000, 20)]
    [DataRow(100000, 20)]
    public async Task ManyZonesAndHistoricalObservationsLoadWithExactCounts(int listingCount, int historyRuns)
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        var now = DateTimeOffset.UtcNow;
        Guid agentId = Guid.CreateVersion7();
        Guid[] groups = Enumerable.Range(0, listingCount >= 100000 ? 100 : 50).Select(_ => Guid.CreateVersion7()).ToArray();
        var searches = groups.Select((group, i) => new SearchConfiguration
        {
            Id = Guid.CreateVersion7(), OrganizationId = f.OrganizationId, SearchGroupId = group,
            Label = "Load search " + i, Source = CatalogSource.Avito, Url = "https://www.avito.ru/test"
        }).ToArray();
        var jobs = searches.Select(search => new ServerCollectionJob
        {
            Id = Guid.CreateVersion7(), OrganizationId = f.OrganizationId, SearchId = search.Id,
            CreatedAt = now, State = CollectionJobState.Completed
        }).ToArray();
        var listings = Enumerable.Range(0, listingCount).Select(i => new Listing
        {
            Id = Guid.CreateVersion7(), OrganizationId = f.OrganizationId, Source = CatalogSource.Avito,
            ExternalId = "load-" + i, Title = "Участок ИЖС " + i, Location = "Тестовая зона",
            Price = 1000000m + i, AreaSquareMeters = 1000m, ReceivedAt = now, ChangedAt = now, RecordedAt = now
        }).ToArray();
        await using (var db = f.Sandbox.Context())
        {
            db.CollectorAgents.Add(new() { Id = agentId, OrganizationId = f.OrganizationId, Name = "Load test" });
            db.SearchGroups.AddRange(groups.Select((id, i) => new SearchGroup
                { Id = id, OrganizationId = f.OrganizationId, Name = "Load zone " + i, RecordedAt = now }));
            db.SearchConfigurations.AddRange(searches);
            db.CollectionJobs.AddRange(jobs);
            db.Listings.AddRange(listings);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            // Set-based synthetic seed only. Runtime reads retain the product's 10-second timeout.
            // Each chunk writes at most 50k real history rows; no product ingress code is changed.
            db.Database.SetCommandTimeout(60);
            Guid[] jobIds = jobs.Select(item => item.Id).ToArray();
            int seedOffset = 0;
            foreach (Guid[] chunk in listings.Select(item => item.Id).Chunk(2500))
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO catalog.observations
                        (id,listing_id,job_id,agent_id,observation_key,content_hash,payload_json,changes_json,observed_at,recorded_at)
                    SELECT uuidv7(), ids.listing_id,
                        ({jobIds})[(((ids.ordinal-1+{seedOffset}) % {jobIds.Length})+1)::int], {agentId},
                        'load-'||(ids.ordinal-1+{seedOffset})::text||'-'||r.repeat::text, 'load',
                        jsonb_build_object(), jsonb_build_array(), {now}-r.repeat*interval '1 day', {now}
                    FROM unnest({chunk}) WITH ORDINALITY AS ids(listing_id,ordinal)
                    CROSS JOIN generate_series(0,{historyRuns}-1) AS r(repeat)
                    """);
                seedOffset += chunk.Length;
            }

        }
        var criteria = new IncomingFilterPresetCriteriaV1(2, null, null, CatalogDisposition.Incoming,
            CatalogAgeRange.Any, null, null, null, null, null, null, [], false,
            IncomingCatalogSortField.ChangedAt, IncomingCatalogSortDirection.Descending);
        await using var source = NpgsqlDataSource.Create(f.Sandbox.RuntimeConnection);
        var presets = new IncomingFilterPresetService(source, new EmployeeAccessService(f.Factory));
        foreach (var group in groups)
            await presets.CreateAsync(f.Manager, new(group, "Load filter", criteria with { SearchGroupId = group, LandTypes = [IncomingLandType.Izhs], MinTotalPrice = 500000m, MaxPricePerSotka = 500000m }), CancellationToken.None);
        await using (var db = f.Sandbox.Context())
            await db.Database.ExecuteSqlRawAsync("ANALYZE");

        var saved = await presets.ReadAsync(f.Manager, CancellationToken.None);
        var measurements = new List<object>();
        string directory = Path.Combine(FoundationTests.RepositoryRoot(), "artifacts", "incoming-package-a", "measurements-final", $"{listingCount}-{historyRuns}");
        Directory.CreateDirectory(directory);
        foreach (var scenario in new[] { "page", "selected-page", "all-page", "information", "full" })
        for (int repeat = 0; repeat < 5; repeat++)
        {
            var probe = new Probe();
            var factory = new ProbeFactory(f.Sandbox.RuntimeConnection, probe);
            var reads = new IncomingCatalogReadService(factory, new EmployeeAccessService(factory), f.Workspace, TimeProvider.System, presets);
            var request = new IncomingCatalogReadFilter(new(Size: 25), WorkingScope: new(
                scenario == "all-page" ? IncomingCatalogMode.AllListings : IncomingCatalogMode.SavedFilters,
                SelectedPresetId: scenario == "selected-page" ? saved[0].Id : null));
            var clock = Stopwatch.StartNew();
            if (scenario == "information")
            {
                var information = await reads.ReadInformationAsync(f.Manager, request, CancellationToken.None);
                Assert.AreEqual(listingCount, information.Summary.New);
                Assert.AreEqual(listingCount, information.FilterCounts!.AllPresetsCount);
                Assert.HasCount(groups.Length, information.FilterCounts.GroupCounts);
                Assert.IsTrue(information.FilterCounts.PresetCounts.Values.All(value => value == listingCount / groups.Length));
            }
            else
            {
                var page = scenario == "full" ? await reads.ReadAsync(f.Manager, request, CancellationToken.None)
                    : await reads.ReadPageAsync(f.Manager, request, CancellationToken.None);
                Assert.AreEqual(scenario == "selected-page" ? listingCount / groups.Length : listingCount, page.Total);
                Assert.HasCount(25, page.Items);
                if (scenario == "full") Assert.AreEqual(page.Total, page.Summary.New);
            }
            long elapsed = clock.ElapsedMilliseconds;
            measurements.Add(new { Scenario = scenario, Repeat = repeat, TotalMs = elapsed, probe.Commands });
            Console.WriteLine($"{scenario}/{repeat}: {elapsed} ms; EF readers={probe.Commands.Count}; max SQL={probe.Commands.Select(command => command.Milliseconds).DefaultIfEmpty().Max():F0} ms");
            await File.WriteAllTextAsync(Path.Combine(directory, "package-a.json"), JsonSerializer.Serialize(measurements));
            Assert.IsFalse(probe.Commands.Any(command => command.Failed));
            if (repeat == 4 && probe.CountSql != null)
            {
                await using var connection = await source.OpenConnectionAsync();
                await using var command = new NpgsqlCommand("EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + probe.CountSql, connection) { CommandTimeout = 10 };
                foreach (var parameter in probe.CountParameters) command.Parameters.Add(parameter.Clone());
                string plan = (string)(await command.ExecuteScalarAsync())!;
                await File.WriteAllTextAsync(Path.Combine(directory, scenario + "-plan.json"), plan);
            }
        }
        // Fresh real service calls on distinct contexts, alongside real Collector ingress.
        var concurrentClock = Stopwatch.StartNew();
        var concurrentReaders = Enumerable.Range(0, 4).Select(async _ =>
        {
            var reads = new IncomingCatalogReadService(f.Factory, new EmployeeAccessService(f.Factory), f.Workspace, TimeProvider.System, presets);
            var request = new IncomingCatalogReadFilter(new(Size: 25), WorkingScope: new(IncomingCatalogMode.SavedFilters));
            var start = Stopwatch.StartNew();
            var page = await reads.ReadPageAsync(f.Manager, request, CancellationToken.None);
            Assert.AreEqual(listingCount, page.Total);
            return start.ElapsedMilliseconds;
        }).ToArray();
        var ingress = IngestAsync(f);
        long[] concurrentMs = await Task.WhenAll(concurrentReaders);
        await ingress;
        await File.WriteAllTextAsync(Path.Combine(directory, "concurrent.json"), JsonSerializer.Serialize(new
        { ReaderMilliseconds = concurrentMs, CombinedMilliseconds = concurrentClock.ElapsedMilliseconds, CollectorIngressCompleted = true }));
        Console.WriteLine("Concurrent reader milliseconds: " + string.Join(", ", concurrentMs));
    }

    private static async Task IngestAsync(ProcurementTests.Phase1Fixture f)
    {
        var administration = new CollectionAdministration(f.Factory, TimeProvider.System);
        var gateway = new CollectorGateway(f.Factory, TimeProvider.System);
        var agent = await administration.CreateAgentAsync(f.Owner, "Concurrent Collector", false, "load", default);
        await gateway.RegisterAsync(agent, new(1, "load", [ListingSource.Avito]), default);
        await administration.CreateSearchAsync(f.Owner, new("Concurrent search", CatalogSource.Avito,
            "https://www.avito.ru/moskva/zemelnye_uchastki", 1), "load", default);
        var search = (await administration.ReadAsync(f.Owner, default)).Searches.Single(item => item.Label == "Concurrent search");
        await administration.EnqueueAsync(f.Owner, search.Id, "load", default);
        var work = (await gateway.ClaimAsync(agent, default))!;
        ListingData data = new()
        {
            Source = ListingSource.Avito, ExternalId = "99000001",
            Url = "https://www.avito.ru/moskva/zemelnye_uchastki/99000001", ObservedAt = DateTimeOffset.UtcNow,
            AdapterVersion = "load", Provenance = "Synthetic concurrent test",
            Title = new(FieldPresence.Present, "ИЖС"), Price = new(FieldPresence.Present, "1000000", 1000000m),
            AreaSquareMeters = new(FieldPresence.Present, "1000", 1000m), Location = new(FieldPresence.Present, "Тест")
        };
        var receipt = await gateway.AcceptAsync(agent, new(Guid.CreateVersion7(), work.JobId, work.LeaseId,
            CollectionOutcome.Success, [new("concurrent-1", data)], true), default);
        Assert.IsNotNull(receipt);
    }

    private sealed class ProbeFactory(string connection, Probe probe) : IDbContextFactory<LandErpDbContext>
    {
        public LandErpDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<LandErpDbContext>();
            LandErpDbContext.Configure(options, connection);
            options.AddInterceptors(probe);
            return new(options.Options);
        }
    }

    private sealed class Probe : DbCommandInterceptor
    {
        public sealed record Timing(string Stage, double Milliseconds, int SqlLength, bool Failed);
        public List<Timing> Commands { get; } = [];
        public string? CountSql { get; private set; }
        public NpgsqlParameter[] CountParameters { get; private set; } = [];
        private static string Stage(DbCommand command) => command.CommandText.Contains("count(", StringComparison.OrdinalIgnoreCase)
            && command.CommandText.Length > 5000 ? "all-counts" : command.CommandText.Contains("LIMIT", StringComparison.Ordinal) ? "page" : "metadata";
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Stage(command) == "all-counts")
            {
                CountSql = command.CommandText;
                CountParameters = command.Parameters.Cast<NpgsqlParameter>().Select(parameter => parameter.Clone()).ToArray();
            }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            Commands.Add(new(Stage(command), eventData.Duration.TotalMilliseconds, command.CommandText.Length, false));
            return ValueTask.FromResult(result);
        }
        public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(new(Stage(command), eventData.Duration.TotalMilliseconds, command.CommandText.Length, true));
            Console.WriteLine($"Failed command: {command.CommandText.Length} characters, {eventData.Duration.TotalMilliseconds:F0} ms");
            return Task.CompletedTask;
        }
    }
}
