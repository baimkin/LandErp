using System.Linq.Expressions;
using System.Reflection;
using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Infrastructure.Modules.Catalog;
using LandErp.Infrastructure.Modules.IdentityAccess;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace LandErp.Foundation.Tests;

[TestClass]
[TestCategory("PostgreSQL")]
public sealed class IncomingPackageATests
{
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [TestMethod]
    public async Task QueryLocalSetsMatchAuthoritativePredicatesAcrossCriteriaModesAndTime()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        var pair = await f.IngestMarketplacePairAsync();
        Guid a = await pair.Administration.CreateGroupAsync(f.Owner, "A", 10, "a", default);
        Guid b = await pair.Administration.CreateGroupAsync(f.Owner, "B", 20, "b", default);
        DateTimeOffset now = new(2026, 11, 23, 20, 59, 59, TimeSpan.Zero);
        var clock = new Clock(now);
        Guid directSearch;
        List<Guid> manualIds = [];
        await using (var db = f.Sandbox.Context())
        {
            foreach (var search in await db.SearchConfigurations.ToArrayAsync())
                search.SearchGroupId = search.Source == CatalogSource.Avito ? a : b;
            directSearch = await db.SearchConfigurations.Where(item => item.Source == CatalogSource.Avito).Select(item => item.Id).SingleAsync();
            var cianJob = await (from job in db.CollectionJobs join search in db.SearchConfigurations on job.SearchId equals search.Id
                where search.Source == CatalogSource.Cian select job).FirstAsync();
            var original = await db.ListingObservations.FirstAsync(item => item.ListingId == pair.AvitoId);
            db.ListingObservations.Add(new() { Id = Guid.CreateVersion7(), ListingId = pair.AvitoId, JobId = cianJob.Id,
                AgentId = original.AgentId, ObservationKey = "multi-zone", ContentHash = "other", PayloadJson = "{}",
                ObservedAt = now.AddDays(-2), RecordedAt = now });
            for (int i = 0; i < 24; i++)
            {
                Guid id = Guid.CreateVersion7();
                manualIds.Add(id);
                var item = new Listing
                {
                    Id = id, OrganizationId = f.OrganizationId, Source = CatalogSource.Telegram,
                    Title = i % 2 == 0 ? "ИЖС участок" : "СНТ участок", Description = "ЛПХ",
                    DeclaredLandTypes = [nameof(IncomingLandType.Kfh)], Location = i % 4 == 0 ? null : "Москва",
                    ExternalId = "manual-" + i, SellerName = "Продавец", CadastralNumber = "50:01:0000001:" + i,
                    Price = i % 6 == 0 ? null : i * 100000m, AreaSquareMeters = i % 5 == 0 ? 0 : 1000m,
                    Disposition = (CatalogDisposition)(i % Enum.GetValues<CatalogDisposition>().Length),
                    AttentionRequired = i % 2 == 0, IncludeInCalculation = i % 3 == 0,
                    ReceivedAt = now.AddDays(-(i % 9)), ChangedAt = now.AddMinutes(-i), RecordedAt = now
                };
                db.Listings.Add(item);
                var kind = Enum.GetValues<CatalogEventKind>()[i % Enum.GetValues<CatalogEventKind>().Length];
                db.CatalogEvents.Add(new() { Id = Guid.CreateVersion7(), OrganizationId = f.OrganizationId,
                    CatalogItemId = id, Kind = kind, Message = "цена", PreviousObservedPrice = 300000m,
                    ObservedPrice = 200000m, RecordedAt = now.AddHours(-(i % 2) * 24) });
            }
            Guid commentType = Guid.CreateVersion7();
            db.ListingCommentTypes.Add(new() { Id = commentType, OrganizationId = f.OrganizationId, Name = "Проверка" });
            db.ListingComments.Add(new() { Id = Guid.CreateVersion7(), OrganizationId = f.OrganizationId,
                ListingId = manualIds[0], CommentTypeId = commentType, Text = "Покрытие поиска комментариев",
                CreatedByEmployeeId = f.ManagerEmployeeId, UpdatedByEmployeeId = f.ManagerEmployeeId, CreatedAt = now, UpdatedAt = now });
            db.CatalogDuplicateCandidates.Add(new() { Id = Guid.CreateVersion7(), OrganizationId = f.OrganizationId,
                ListingId = manualIds[0], CandidateListingId = manualIds[1], RecordedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync();
        }
        await using var source = NpgsqlDataSource.Create(f.Sandbox.RuntimeConnection);
        var presets = new IncomingFilterPresetService(source, new EmployeeAccessService(f.Factory));
        var criteria = new IncomingFilterPresetCriteriaV1(2, null, null, CatalogDisposition.Incoming, CatalogAgeRange.Any,
            null, null, null, null, null, null, [], false, IncomingCatalogSortField.ChangedAt, IncomingCatalogSortDirection.Descending);
        var first = await presets.CreateAsync(f.Manager, new(a, "A narrow", criteria with { SearchGroupId = a, MaxTotalPrice = 3000000m }), default);
        await presets.CreateAsync(f.Manager, new(a, "A wide", criteria with { SearchGroupId = a }), default);
        await presets.CreateAsync(f.Manager, new(b, "B filter", criteria with { SearchGroupId = b }), default);
        await presets.CreateAsync(f.Manager, new(null, "Unbounded", criteria), default);
        var reads = new IncomingCatalogReadService(f.Factory, new EmployeeAccessService(f.Factory), f.Workspace, clock, presets);
        List<IncomingCatalogReadFilter> requests =
        [
            new(new(Size: 100), WorkingScope: new()),
            new(new(Size: 100), SearchGroupId: a, WorkingScope: new()),
            new(new(Size: 100), SearchGroupId: b, WorkingScope: new(SelectedPresetId: first.Id)),
            new(new(Size: 100, Disposition: null, MaxPrice: 9000000m), WorkingScope: new(SelectedPresetId: first.Id, UseDraft: true)),
            new(new(Size: 100), SearchGroupId: a, WorkingScope: new(IncomingCatalogMode.Participants)),
            new(new(Size: 100, MaxPrice: 1m), WorkingScope: new(IncomingCatalogMode.Archive)),
            new(new(Size: 100), WorkingScope: new(IncomingCatalogMode.AllListings)),
            new(new("Продавец manual", Disposition: null, Size: 100))
        ];
        requests.Add(new(new("Продавец покрытие", Disposition: null, Size: 100)));
        requests.Add(new(new(Disposition: null, Size: 100), SearchConfigurationId: directSearch));
        requests.AddRange(Enum.GetValues<IncomingCatalogPreset>().Select(slice => new IncomingCatalogReadFilter(
            new(Size: 100), WorkingScope: new(Slice: slice))));
        requests.AddRange(Enum.GetValues<CatalogAgeRange>().Select(age => new IncomingCatalogReadFilter(new(Age: age, Size: 100))));
        requests.AddRange(Enum.GetValues<IncomingLandType>().Select(type => new IncomingCatalogReadFilter(new(Disposition: null, Size: 100), LandTypes: [type])));
        requests.AddRange(Enum.GetValues<IncomingCatalogPreset>().Select(preset => new IncomingCatalogReadFilter(new(Disposition: null, Size: 100), Preset: preset)));
        requests.Add(new(new(Disposition: null, MinPrice: 100000m, MaxPrice: 2000000m, MinAreaSquareMeters: 1, MaxAreaSquareMeters: 1000, AttentionOnly: true, Size: 100),
            MinPricePerSotka: 10000m, MaxPricePerSotka: 200000m, IncludedInMedian: true));
        requests.Add(new(new(Source: CatalogSource.Avito, Disposition: null, Size: 100), IncludedInMedian: false));
        foreach (var request in requests) await CompareAsync(f, reads, presets, request, clock.Now);

        // Time alone changes Age and the Moscow calendar cohort; no maintenance write occurs.
        clock.Now = now.AddSeconds(2);
        foreach (var request in requests.Where(item => item.Base.Age != CatalogAgeRange.Any
            || item.Preset == IncomingCatalogPreset.ProcessedToday || item.WorkingScope?.Slice == IncomingCatalogPreset.ProcessedToday))
            await CompareAsync(f, reads, presets, request, clock.Now);
        var main = await reads.ReadPageAsync(f.Manager, requests[0], default);
        var sorted = await reads.ReadPageAsync(f.Manager, requests[0] with { SortField = IncomingCatalogSortField.Price }, default);
        Assert.AreEqual(main.InformationScopeKey, sorted.InformationScopeKey);
        var info = await reads.ReadInformationAsync(f.Manager, requests[0], default);
        Assert.AreEqual(main.InformationScopeKey, info.ScopeKey);
        var clamp = await reads.ReadPageAsync(f.Manager, requests[0] with { Base = new(Size: 25, Offset: 5000) }, default);
        Assert.AreEqual(main.Total == 0 ? 0 : (main.Total - 1) / 25 * 25, clamp.EffectiveOffset);
        var foreign = await reads.ReadPageAsync(f.ForeignOwner, requests[0], default);
        Assert.AreEqual(0, foreign.Total);
        Assert.AreNotEqual(main.InformationScopeKey, foreign.InformationScopeKey);
    }

    // Reflection keeps the command predicate internal to the product. This is the unchanged
    // authoritative path (optimizeRead=false), not a second hand-written business implementation.
    private static async Task CompareAsync(ProcurementTests.Phase1Fixture f, IncomingCatalogReadService reads,
        IncomingFilterPresetService presets, IncomingCatalogReadFilter filter, DateTimeOffset now)
    {
        var saved = filter.WorkingScope == null || filter.WorkingScope.Mode is IncomingCatalogMode.Archive or IncomingCatalogMode.Participants
            ? Array.Empty<IncomingFilterPresetView>() : (await presets.ReadAsync(f.Manager, default)).ToArray();
        await using var db = f.Sandbox.Context(runtime: true);
        Type type = typeof(IncomingCatalogReadService).Assembly.GetType("LandErp.Infrastructure.Modules.Catalog.IncomingCatalogSelection")!;
        var task = (Task)type.GetMethod("PrepareAsync", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [db, f.OrganizationId, now, filter, saved, CancellationToken.None, false, true])!;
        await task;
        object selection = task.GetType().GetProperty("Result")!.GetValue(task)!;
        T Get<T>(string property) => (T)type.GetProperty(property)!.GetValue(selection)!;
        var query = db.Listings.AsNoTracking().Where(item => item.OrganizationId == f.OrganizationId);
        Guid[] expected = await query.Where(Get<Expression<Func<Listing, bool>>>("Current")).Select(item => item.Id).ToArrayAsync();
        var page = await reads.ReadAsync(f.Manager, filter, default);
        Assert.AreEqual(expected.Length, page.Total, filter.ToString());
        CollectionAssert.AreEquivalent(expected, page.Items.Select(item => item.Id).ToArray(), filter.ToString());
        var contextFor = Get<Func<IncomingCatalogPreset?, Expression<Func<Listing, bool>>>>("ContextFor");
        int[] summary = [];
        List<int> values = [];
        foreach (var slice in Enum.GetValues<IncomingCatalogPreset>()) values.Add(await query.Where(contextFor(slice)).CountAsync());
        summary = [page.Summary.New, page.Summary.PriceChanged, page.Summary.PossibleDuplicate,
            page.Summary.Incomplete, page.Summary.ReturnedFromMonitoring, page.Summary.ProcessedToday];
        CollectionAssert.AreEqual(values.ToArray(), summary);
        Assert.AreEqual(await query.Where(contextFor(null)).CountAsync(), page.Summary.Incoming);
        if (page.FilterCounts == null) return;
        object predicates = type.GetProperty("Predicates")!.GetValue(selection)!;
        var conditionMethod = predicates.GetType().GetMethod("Conditions")!;
        var sliceMethod = predicates.GetType().GetMethod("Slice")!;
        var slicePredicate = (Expression<Func<Listing, bool>>)sliceMethod.Invoke(predicates, [filter.WorkingScope?.Slice])!;
        Dictionary<Guid, HashSet<Guid>> groups = [];
        HashSet<Guid> ungrouped = [];
        foreach (var preset in saved.Where(item => item.CompatibilityIssue == null))
        {
            var c = preset.Criteria;
            var presetFilter = new IncomingCatalogReadFilter(new(filter.Base.Text, c.Source, c.Disposition, c.Age,
                c.MinTotalPrice, c.MaxTotalPrice, c.MinAreaSquareMeters, c.MaxAreaSquareMeters, c.AttentionOnly),
                c.SearchConfigurationId, c.Preset, c.SortField, c.SortDirection, c.SearchGroupId,
                c.MinPricePerSotka, c.MaxPricePerSotka, c.LandTypes, IncludedInMedian: c.IncludedInMedian);
            var predicate = (Expression<Func<Listing, bool>>)conditionMethod.Invoke(predicates, [presetFilter])!;
            Guid[] ids = await query.Where(predicate).Where(slicePredicate).Select(item => item.Id).ToArrayAsync();
            Assert.AreEqual(ids.Length, page.FilterCounts.PresetCounts[preset.Id]);
            if (c.SearchGroupId is Guid group)
            {
                if (!groups.TryGetValue(group, out var set)) groups[group] = set = [];
                set.UnionWith(ids);
            }
            else ungrouped.UnionWith(ids);
        }
        foreach (var (group, ids) in groups) Assert.AreEqual(ids.Count, page.FilterCounts.GroupCounts[group]);
        Assert.AreEqual(ungrouped.Count, page.FilterCounts.UngroupedCount);
    }
}
