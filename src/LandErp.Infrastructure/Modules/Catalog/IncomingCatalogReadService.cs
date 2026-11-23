using LandErp.Application.Foundation;
using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Infrastructure.Modules.IdentityAccess;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using Npgsql;
using System.Linq.Expressions;
using static LandErp.Infrastructure.Modules.Catalog.IncomingCatalogQuery;

namespace LandErp.Infrastructure.Modules.Catalog;

/// <summary>
/// Read-only projections for Incoming V2. Derives working views from facts already persisted by Catalog/Collection;
/// it deliberately does not own commands or introduce Listing persistence fields.
/// </summary>
public sealed class IncomingCatalogReadService(
    IDbContextFactory<LandErpDbContext> factory,
    IEmployeeAccessService employeeAccess,
    ICatalogWorkspace catalogWorkspace,
    TimeProvider time,
    IIncomingFilterPresetService? filterPresets = null) : IIncomingCatalogReadService
{
    public IncomingCatalogReadService(IDbContextFactory<LandErpDbContext> factory,
        ICatalogWorkspace catalogWorkspace, TimeProvider time)
        : this(factory, new EmployeeAccessService(factory), catalogWorkspace, time) { }

    private async Task<EffectiveEmployeeAccess> RequireReadAsync(Subject subject, CancellationToken cancellationToken)
    {
        EffectiveEmployeeAccess effective = await employeeAccess.ResolveAsync(subject, cancellationToken);
        if (!effective.CanReadIncoming) throw new AccessDeniedException();
        return effective;
    }
    // Compatibility composition for existing callers. The UI requests page and information independently.
    public Task<IncomingCatalogReadPage> ReadAsync(Subject subject, IncomingCatalogReadFilter filter, CancellationToken cancellationToken)
        => ReadCoreAsync(subject, filter, true, false, cancellationToken);
    public Task<IncomingCatalogReadPage> ReadPageAsync(Subject subject, IncomingCatalogReadFilter filter, CancellationToken cancellationToken)
        => ReadCoreAsync(subject, filter, false, false, cancellationToken);
    public async Task<IncomingCatalogInformation> ReadInformationAsync(Subject subject, IncomingCatalogReadFilter filter, CancellationToken cancellationToken)
    {
        var result = await ReadCoreAsync(subject, filter, true, true, cancellationToken);
        return new(result.Summary, result.FilterCounts, result.AsOf, result.InformationScopeKey);
    }
    private async Task<IncomingCatalogReadPage> ReadCoreAsync(Subject subject, IncomingCatalogReadFilter filter,
        bool includeInformation, bool informationOnly, CancellationToken cancellationToken)
    {
        EffectiveEmployeeAccess effective = await RequireReadAsync(subject, cancellationToken);
        AccessContext context = effective.OrganizationContext;
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", cancellationToken);
        var presets = await ReadPresetsAsync(db, subject, context.OrganizationId, filter, cancellationToken);
        DateTimeOffset now = time.GetUtcNow();
        var prepared = await IncomingCatalogSelection.PrepareAsync(db, context.OrganizationId, now, filter,
            presets, cancellationToken, optimizeRead: true, includeAlternatives: includeInformation);
        filter = prepared.Filter;
        var scope = filter.WorkingScope;
        bool archive = scope?.Mode == IncomingCatalogMode.Archive;
        var predicates = prepared.Predicates;
        var applicable = prepared.Applicable;
        var union = prepared.Union;
        var current = prepared.Current;
        var ContextFor = prepared.ContextFor;
        var activeSlice = scope?.Slice;
        IQueryable<Listing> organizationItems = db.Listings.AsNoTracking().Where(item => item.OrganizationId == context.OrganizationId);
        int total = 0;
        IncomingCatalogReadSummary summary = new(0, 0, 0, 0, 0, 0, 0);
        IncomingCatalogFilterCounts? filterCounts = scope == null || archive || prepared.ParticipantTotal != null ? null
            : new(presets, new Dictionary<Guid, int>(), new Dictionary<Guid, int>(), 0, 0, applicable.Length);
        if (includeInformation)
        {
            // Each tile replaces only the page slice. Persisted preset criteria still participate in AND.
            List<Expression<Func<Listing, bool>>> countPredicates = [current, ContextFor(null)];
            IncomingCatalogPreset[] slices = Enum.GetValues<IncomingCatalogPreset>();
            countPredicates.AddRange(slices.Select(slice => ContextFor(slice)));
            Expression<Func<Listing, bool>> contextWithoutSlice = ContextFor(null);
            countPredicates.Add(And(contextWithoutSlice, item => item.AttentionRequired));
            countPredicates.Add(And(contextWithoutSlice, item => item.Disposition == CatalogDisposition.Monitoring));
            countPredicates.Add(And(contextWithoutSlice, item => item.Disposition == CatalogDisposition.InWork));
            int presetOffset = countPredicates.Count;
            prepared.ReadSql!.AddAlternatives(And(
                predicates.Conditions(new(new(filter.Base.Text, Disposition: null))), predicates.Slice(activeSlice)));
            var presetGroups = applicable.GroupBy(item => item.Criteria.SearchGroupId).ToArray();
            int groupOffset = presetOffset + applicable.Length;
            int allPresetsOffset = groupOffset + presetGroups.Length;
            countPredicates.Add(And(And(union, predicates.Conditions(new(new(filter.Base.Text,
                Disposition: activeSlice == null ? CatalogDisposition.Incoming : null), SearchGroupId: filter.SearchGroupId))),
                predicates.Slice(activeSlice)));
            int[] counts = await prepared.ReadSql!.CountInformationAsync(organizationItems, countPredicates,
                applicable, presetGroups.Select(group => group.Key).ToArray(), cancellationToken);
            int SliceCount(IncomingCatalogPreset slice) => counts[2 + Array.IndexOf(slices, slice)];
            summary = new(counts[1], counts[8], counts[9], counts[10],
                SliceCount(IncomingCatalogPreset.Incomplete), SliceCount(IncomingCatalogPreset.PriceChanged),
                SliceCount(IncomingCatalogPreset.ReturnedFromMonitoring), SliceCount(IncomingCatalogPreset.New),
                SliceCount(IncomingCatalogPreset.ProcessedToday), SliceCount(IncomingCatalogPreset.PossibleDuplicate));
            Dictionary<Guid, int> presetCounts = [];
            for (int index = 0; index < applicable.Length; index++) presetCounts[applicable[index].Id] = counts[presetOffset + index];
            Dictionary<Guid, int> filterGroupCounts = [];
            int ungroupedCount = 0;
            for (int index = 0; index < presetGroups.Length; index++)
                if (presetGroups[index].Key is Guid groupId) filterGroupCounts[groupId] = counts[groupOffset + index];
                else ungroupedCount = counts[groupOffset + index];
            filterCounts = scope == null || archive || prepared.ParticipantTotal != null ? null
                : new(presets, presetCounts, filterGroupCounts, ungroupedCount, counts[allPresetsOffset], applicable.Length);
            total = counts[0];
        }
        else
        {
            // Only the effective WHERE belongs on the critical path, not the alternative metrics.
            total = (await prepared.ReadSql!.CountAsync(organizationItems.Where(current), [item => true], cancellationToken))[0];
        }
        int size = filter.Base.Size;
        int offset = total == 0 ? 0 : Math.Min(filter.Base.Offset, (total - 1) / size * size);
        prepared = prepared with { Filter = filter with { Base = filter.Base with { Offset = offset } } };
        IncomingCatalogReadWindow window = informationOnly ? new([], new Dictionary<Guid, IncomingCatalogRowRead>())
            : await ReadWindowCoreAsync(db, context.OrganizationId, prepared, cancellationToken);

        var groupMetadata = await db.SearchGroups.AsNoTracking().Where(item => item.OrganizationId == context.OrganizationId)
            .OrderBy(item => item.SortOrder).ThenBy(item => item.Name).ThenBy(item => item.Id)
            .Select(item => new { item.Id, item.Version, item.Active,
                View = new IncomingSearchGroupView(item.Id, item.Name, item.SortOrder, item.OwnerEmployeeId,
                    effective.CanManageAllSearchGroups || effective.Settings.CanManageSearchGroups && item.OwnerEmployeeId == effective.EmployeeId) })
            .ToArrayAsync(cancellationToken);
        var searchMetadata = await db.SearchConfigurations.AsNoTracking().Where(item => item.OrganizationId == context.OrganizationId)
            .OrderBy(item => item.Label).ThenBy(item => item.Id)
            .Select(item => new { item.Id, item.Version, item.Enabled,
                View = new IncomingSearchConfigurationView(item.Id, item.Label, item.Source, item.SearchGroupId) })
            .ToArrayAsync(cancellationToken);
        var normalized = filter with { Base = filter.Base with { Offset = 0, Size = 0 },
            SortField = IncomingCatalogSortField.ChangedAt, SortDirection = IncomingCatalogSortDirection.Descending,
            LandTypes = filter.LandTypes?.Order().ToArray() };
        string scopeKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            context.OrganizationId, effective.EmployeeId, effective.OrganizationContext, Filter = normalized,
            Presets = presets.Select(item => new { item.Id, item.Version, item.CompatibilityIssue }),
            Groups = groupMetadata.Select(item => new { item.Id, item.Version, item.Active }),
            Searches = searchMetadata.Select(item => new { item.Id, item.Version, item.Enabled })
        }))));
        await transaction.CommitAsync(cancellationToken);
        return new(window.Items, total, summary, groupMetadata.Where(item => item.Active).Select(item => item.View).ToArray(),
            searchMetadata.Where(item => item.Enabled).Select(item => item.View).ToArray(), window.Rows, filterCounts,
            prepared.ParticipantTotal, offset, scopeKey) { AsOf = now };
    }

    private async Task<IReadOnlyList<IncomingFilterPresetView>> ReadPresetsAsync(LandErpDbContext db, Subject subject,
        Guid organizationId, IncomingCatalogReadFilter filter, CancellationToken cancellationToken)
    {
        if (filter.WorkingScope == null || filter.WorkingScope.Mode is IncomingCatalogMode.Archive or IncomingCatalogMode.Participants) return [];
        if (filterPresets is IncomingFilterPresetService)
            return await IncomingFilterPresetService.ReadForOrganizationAsync((NpgsqlConnection)db.Database.GetDbConnection(), organizationId, cancellationToken);
        return await (filterPresets ?? throw new InvalidOperationException("Saved-filter service is required."))
            .ReadAsync(subject, cancellationToken);
    }

    public async Task<IncomingCatalogReadWindow> ReadWindowAsync(Subject subject, IncomingCatalogReadFilter filter,
        CancellationToken cancellationToken)
    {
        EffectiveEmployeeAccess effective = await RequireReadAsync(subject, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        var prepared = await PrepareWindowAsync(db, subject, effective.OrganizationContext.OrganizationId, filter, cancellationToken);
        return await ReadWindowCoreAsync(db, effective.OrganizationContext.OrganizationId, prepared, cancellationToken);
    }

    public async Task<IncomingCatalogReadWindow> ReadItemsAsync(Subject subject, IncomingCatalogReadFilter filter,
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        EffectiveEmployeeAccess effective = await RequireReadAsync(subject, cancellationToken);
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count > 100) throw new ArgumentException("Окно обновления ограничено 100 строками.", nameof(ids));
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        var bounded = filter with { Base = filter.Base with { Offset = 0, Size = 100 } };
        var prepared = await PrepareWindowAsync(db, subject, effective.OrganizationId, bounded, cancellationToken);
        return await ReadWindowCoreAsync(db, effective.OrganizationId, prepared, cancellationToken, ids.ToArray());
    }

    public async Task<IncomingCatalogChangeScope> ReadChangeScopeAsync(Subject subject, IncomingCatalogReadFilter filter,
        Guid catalogItemId, CancellationToken cancellationToken)
    {
        EffectiveEmployeeAccess effective = await RequireReadAsync(subject, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        var item = await db.Listings.AsNoTracking().Where(row => row.OrganizationId == effective.OrganizationId && row.Id == catalogItemId)
            .Select(row => new { row.Id, row.ObjectGroupId }).SingleOrDefaultAsync(cancellationToken) ?? throw new AccessDeniedException();
        // Transfer affects the confirmed source group, including sources outside the visible page.
        var sources = db.Listings.AsNoTracking().Where(row => row.OrganizationId == effective.OrganizationId);
        sources = item.ObjectGroupId is Guid groupId ? sources.Where(row => row.ObjectGroupId == groupId) : sources.Where(row => row.Id == item.Id);
        Guid[] ids = await sources.Select(row => row.Id).ToArrayAsync(cancellationToken);
        var prepared = await PrepareWindowAsync(db, subject, effective.OrganizationId, filter, cancellationToken);
        Guid[] matching = await sources.Where(prepared.Current).Select(row => row.Id).ToArrayAsync(cancellationToken);
        return new(ids, matching);
    }

    private async Task<IncomingCatalogSelection> PrepareWindowAsync(LandErpDbContext db, Subject subject,
        Guid organizationId, IncomingCatalogReadFilter filter, CancellationToken cancellationToken)
    {
        IReadOnlyList<IncomingFilterPresetView> presets = filter.WorkingScope?.Mode != IncomingCatalogMode.SavedFilters ? []
            : await (filterPresets ?? throw new InvalidOperationException("Saved-filter service is required."))
                .ReadAsync(subject, cancellationToken);
        return await IncomingCatalogSelection.PrepareAsync(db, organizationId, time.GetUtcNow(), filter, presets, cancellationToken);
    }

    private static async Task<IncomingCatalogReadWindow> ReadWindowCoreAsync(LandErpDbContext db, Guid organizationId,
        IncomingCatalogSelection prepared, CancellationToken cancellationToken, Guid[]? onlyIds = null)
    {
        var filter = prepared.Filter;
        var baseFilter = filter.Base;
        var current = prepared.Current;
        var reviewedIds = prepared.ReviewedIds;
        var pendingDuplicateListingIds = prepared.PendingDuplicateListingIds;
        IQueryable<Listing> organizationItems = db.Listings.AsNoTracking().Where(item => item.OrganizationId == organizationId);
        IQueryable<Listing> query = organizationItems.Where(current);
        if (onlyIds != null) query = query.Where(item => onlyIds.Contains(item.Id));
        string[] searchTerms = SearchTerms(baseFilter.Text);
        IOrderedQueryable<Listing> ordered = (filter.SortField, filter.SortDirection) switch
        {
            (IncomingCatalogSortField.PricePerSotka, IncomingCatalogSortDirection.Ascending) => query
                .OrderBy(UnknownPricePerSotka).ThenBy(PricePerSotkaKey).ThenBy(item => item.Id),
            (IncomingCatalogSortField.PricePerSotka, _) => query
                .OrderBy(UnknownPricePerSotka).ThenByDescending(PricePerSotkaKey).ThenBy(item => item.Id),
            (IncomingCatalogSortField.Price, IncomingCatalogSortDirection.Ascending) => query
                .OrderBy(item => item.Price == null).ThenBy(item => item.Price).ThenBy(item => item.Id),
            (IncomingCatalogSortField.Price, _) => query
                .OrderBy(item => item.Price == null).ThenByDescending(item => item.Price).ThenBy(item => item.Id),
            (IncomingCatalogSortField.Area, IncomingCatalogSortDirection.Ascending) => query
                .OrderBy(item => item.AreaSquareMeters == null).ThenBy(item => item.AreaSquareMeters).ThenBy(item => item.Id),
            (IncomingCatalogSortField.Area, _) => query
                .OrderBy(item => item.AreaSquareMeters == null).ThenByDescending(item => item.AreaSquareMeters).ThenBy(item => item.Id),
            (IncomingCatalogSortField.ChangedAt, IncomingCatalogSortDirection.Ascending) => query
                .OrderBy(item => item.ChangedAt).ThenBy(item => item.Id),
            _ => query.OrderByDescending(item => item.ChangedAt).ThenBy(item => item.Id)
        };
        var pageQuery = ordered.Skip(baseFilter.Offset).Take(baseFilter.Size);
        Listing[] items = prepared.ReadSql == null ? await pageQuery.ToArrayAsync(cancellationToken)
            : await prepared.ReadSql.ReadItemsAsync(pageQuery, cancellationToken);
        if (items.Length == 0) return new([], new Dictionary<Guid, IncomingCatalogRowRead>());
        Guid[] ids = items.Select(item => item.Id).ToArray();

        var commentRows = await (from comment in db.ListingComments.AsNoTracking()
                                 join type in db.ListingCommentTypes.AsNoTracking() on comment.CommentTypeId equals type.Id
                                 where comment.OrganizationId == organizationId && ids.Contains(comment.ListingId)
                                 orderby type.SortOrder, type.Name, type.Id
                                 select new { comment.ListingId, Comment = new ListingCommentPreview(
                                     comment.Id, comment.CommentTypeId, type.Name, comment.Text) })
            .ToArrayAsync(cancellationToken);
        Dictionary<Guid, IReadOnlyList<ListingCommentPreview>> commentsByItem = commentRows
            .GroupBy(item => item.ListingId)
            .ToDictionary(group => group.Key,
                group => (IReadOnlyList<ListingCommentPreview>)group.Select(item => item.Comment).ToArray());

        var linked = await (from link in db.PropertyCaseSourceLinks.AsNoTracking()
                            join propertyCase in db.PropertyCases.AsNoTracking() on link.PropertyCaseId equals propertyCase.Id
                            where ids.Contains(link.CatalogItemId) && link.Confirmed
                            select new { link.CatalogItemId, propertyCase.Id, propertyCase.BusinessNumber, propertyCase.StageId })
            .ToArrayAsync(cancellationToken);
        var linksByItem = linked.ToDictionary(item => item.CatalogItemId);

        Guid[] pageGroupIds = items.Where(item => item.ObjectGroupId != null)
            .Select(item => item.ObjectGroupId!.Value).Distinct().ToArray();
        Dictionary<Guid, int> groupCounts = pageGroupIds.Length == 0
            ? []
            : await db.Listings.AsNoTracking().Where(item => item.OrganizationId == organizationId
                    && item.ObjectGroupId != null && pageGroupIds.Contains(item.ObjectGroupId.Value))
                .GroupBy(item => item.ObjectGroupId!.Value)
                .ToDictionaryAsync(group => group.Key, group => group.Count(), cancellationToken);

        var searchRows = await db.Listings.AsNoTracking().Where(item => item.OrganizationId == organizationId && ids.Contains(item.Id))
            .SelectMany(item => (from observation in db.ListingObservations.AsNoTracking()
                join job in db.CollectionJobs.AsNoTracking() on observation.JobId equals job.Id
                join search in db.SearchConfigurations.AsNoTracking() on job.SearchId equals search.Id
                where observation.ListingId == item.Id && search.OrganizationId == organizationId
                orderby observation.ObservedAt descending, observation.Id descending
                select new { ListingId = item.Id, SearchId = search.Id, search.Label }).Take(1))
            .ToArrayAsync(cancellationToken);
        var searchByItem = searchRows.ToDictionary(item => item.ListingId);

        HashSet<Guid> reviewedOnPage = (await reviewedIds.Where(id => ids.Contains(id)).ToArrayAsync(cancellationToken)).ToHashSet();
        HashSet<Guid> possibleDuplicatesOnPage = (await pendingDuplicateListingIds.Where(id => ids.Contains(id))
            .ToArrayAsync(cancellationToken)).ToHashSet();

        var flagRows = await db.CatalogEvents.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && ids.Contains(item.CatalogItemId)
                && (item.Kind == CatalogEventKind.SourceChanged || item.Kind == CatalogEventKind.MonitoringTriggered))
            .GroupBy(item => item.CatalogItemId)
            .Select(group => new { Id = group.Key,
                PriceChanged = group.Any(item => item.Kind == CatalogEventKind.SourceChanged
                    && ((item.PreviousObservedPrice != null && item.ObservedPrice != null && item.PreviousObservedPrice != item.ObservedPrice)
                        || item.Message.Contains("цена"))),
                Returned = group.Any(item => item.Kind == CatalogEventKind.MonitoringTriggered) }).ToArrayAsync(cancellationToken);
        var flagsByItem = flagRows.ToDictionary(item => item.Id);

        Dictionary<Guid, IncomingCatalogRowRead> rows = [];
        CatalogItemView[] views = items.Select(item =>
        {
            linksByItem.TryGetValue(item.Id, out var link);
            searchByItem.TryGetValue(item.Id, out var search);
            flagsByItem.TryGetValue(item.Id, out var flags);
            bool priceChanged = flags?.PriceChanged ?? false;
            bool returned = flags?.Returned ?? false;
            string[] photos = PhotoUrls(item.PhotosJson);
            IncomingLandType[] inferredLandTypes = IncomingLandTypeClassifier.Classify(item.Title, item.Description);
            IncomingLandType[] declaredLandTypes = DeclaredLandTypes(item);
            IncomingLandType[] landTypes = inferredLandTypes;
            bool landTypeConflict = HasLandTypeConflict(declaredLandTypes, inferredLandTypes);
            (IncomingCatalogMatchField? matchedField, string? matchedValue) = SearchMatch(item, searchTerms);
            int objectGroupMemberCount = item.ObjectGroupId is Guid groupId && groupCounts.TryGetValue(groupId, out int count)
                ? count : 0;
            rows[item.Id] = new(item.Id, search?.SearchId, search?.Label, Completeness(item),
                RowState(item, priceChanged, returned), priceChanged, returned, photos.FirstOrDefault(), photos.Length,
                landTypes, matchedField, matchedValue, Reviewed: reviewedOnPage.Contains(item.Id),
                PossibleDuplicate: possibleDuplicatesOnPage.Contains(item.Id),
                ObjectGroupId: item.ObjectGroupId, ObjectGroupMemberCount: objectGroupMemberCount,
                DeclaredLandTypes: declaredLandTypes, LandTypeConflict: landTypeConflict,
                Comments: commentsByItem.GetValueOrDefault(item.Id) ?? []);
            return CatalogView(item, link?.Id, link?.BusinessNumber, link?.StageId, objectGroupMemberCount);
        }).ToArray();

        return new(views, rows);
    }

    public async Task<IncomingCatalogDetailRead> ReadDetailAsync(Subject subject, Guid catalogItemId, CancellationToken cancellationToken)
    {
        CatalogItemDetail detail = await catalogWorkspace.ReadItemAsync(subject, catalogItemId, cancellationToken);
        AccessContext context = (await RequireReadAsync(subject, cancellationToken)).OrganizationContext;
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        Listing item = await db.Listings.AsNoTracking().SingleAsync(value => value.Id == catalogItemId
            && value.OrganizationId == context.OrganizationId, cancellationToken);

        var search = await (from observation in db.ListingObservations.AsNoTracking()
                            join job in db.CollectionJobs.AsNoTracking() on observation.JobId equals job.Id
                            join configuration in db.SearchConfigurations.AsNoTracking() on job.SearchId equals configuration.Id
                            where observation.ListingId == catalogItemId && configuration.OrganizationId == context.OrganizationId
                            orderby observation.ObservedAt descending
                            select new { SearchId = configuration.Id, configuration.Label }).FirstOrDefaultAsync(cancellationToken);
        bool priceChanged = await db.CatalogEvents.AsNoTracking().AnyAsync(value => value.CatalogItemId == catalogItemId
            && value.Kind == CatalogEventKind.SourceChanged && value.Message.Contains("цена"), cancellationToken);
        bool returned = await db.CatalogEvents.AsNoTracking().AnyAsync(value => value.CatalogItemId == catalogItemId
            && value.Kind == CatalogEventKind.MonitoringTriggered, cancellationToken);

        var duplicateRows = await (from duplicate in db.CatalogDuplicateCandidates.AsNoTracking()
                                   join candidate in db.Listings.AsNoTracking() on duplicate.CandidateListingId equals candidate.Id
                                   where duplicate.OrganizationId == context.OrganizationId
                                       && duplicate.ListingId == catalogItemId
                                       && duplicate.Status == DuplicateCandidateStatus.Pending
                                   orderby duplicate.Score descending, duplicate.RecordedAt
                                   select new
                                   {
                                       duplicate.Id, duplicate.Version, duplicate.CandidateListingId, candidate.Source,
                                       candidate.Title, candidate.Location, candidate.Price, candidate.AreaSquareMeters,
                                       candidate.CadastralNumber, candidate.Url, duplicate.ReasonsJson, duplicate.PhotoEvidenceJson, duplicate.RecordedAt,
                                       duplicate.Score
                                   }).ToArrayAsync(cancellationToken);
        IncomingDuplicateCandidateView[] duplicateCandidates = duplicateRows.Select(value =>
            new IncomingDuplicateCandidateView(value.Id, value.Version, value.CandidateListingId, value.Source,
                value.Title ?? "Название неизвестно", value.Location, value.Price, value.AreaSquareMeters,
                value.CadastralNumber, value.Url, DuplicateReasons(value.ReasonsJson), value.RecordedAt, value.Score) { PhotoEvidence = JsonSerializer.Deserialize<DuplicatePhotoEvidence[]>(value.PhotoEvidenceJson) ?? [] }).ToArray();

        for(int index=0;index<duplicateCandidates.Length;index++)
        {
            var candidate=duplicateCandidates[index];
            if(candidate.PhotoEvidence.Count>0)continue;
            var other=await db.Listings.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==candidate.CandidateListingId && x.OrganizationId==context.OrganizationId,cancellationToken);
            if(other!=null)duplicateCandidates[index]=candidate with { PhotoEvidence=await IncomingDuplicateDetector.RebuildPhotoEvidenceAsync(db,item,other,cancellationToken) };
        }

        IncomingListingContactView[] contacts = await db.ListingContacts.AsNoTracking()
            .Where(value => value.OrganizationId == context.OrganizationId && value.ListingId == catalogItemId)
            .OrderByDescending(value => value.IsPrimary).ThenBy(value => value.Type).ThenBy(value => value.DisplayValue)
            .Select(value => new IncomingListingContactView(value.Type, value.Value, value.DisplayValue,
                value.Source, value.IsPrimary, value.FirstObservedAt, value.LastObservedAt))
            .ToArrayAsync(cancellationToken);

        IncomingObjectGroupView? objectGroup = null;
        if (item.ObjectGroupId is Guid objectGroupId)
        {
            Listing[] members = await db.Listings.AsNoTracking()
                .Where(value => value.OrganizationId == context.OrganizationId && value.ObjectGroupId == objectGroupId)
                .OrderBy(value => value.ReceivedAt).ThenBy(value => value.Id).ToArrayAsync(cancellationToken);
            Guid[] memberIds = members.Select(value => value.Id).ToArray();
            var groupLinks = await (from link in db.PropertyCaseSourceLinks.AsNoTracking()
                                    join propertyCase in db.PropertyCases.AsNoTracking() on link.PropertyCaseId equals propertyCase.Id
                                    where memberIds.Contains(link.CatalogItemId) && link.Confirmed
                                    select new { link.CatalogItemId, propertyCase.Id, propertyCase.BusinessNumber })
                .ToArrayAsync(cancellationToken);
            var groupLinksByItem = groupLinks.ToDictionary(value => value.CatalogItemId);
            IncomingObjectGroupMemberView[] memberViews = members.Select(value =>
            {
                groupLinksByItem.TryGetValue(value.Id, out var link);
                return new IncomingObjectGroupMemberView(value.Id, value.Source, value.Title ?? "Название неизвестно",
                    value.Location, value.Price, PricePerSotka(value), value.AreaSquareMeters,
                    value.CadastralNumber, value.Url, value.Disposition, link?.Id, link?.BusinessNumber);
            }).ToArray();
            objectGroup = new(objectGroupId, memberViews.Length, memberViews);
        }

        IncomingLandType[] inferredLandTypes = IncomingLandTypeClassifier.Classify(item.Title, item.Description);
        IncomingLandType[] declaredLandTypes = DeclaredLandTypes(item);
        return new(detail, PhotoUrls(item.PhotosJson), search?.SearchId, search?.Label, Completeness(item),
            RowState(item, priceChanged, returned), priceChanged, returned,
            inferredLandTypes, duplicateCandidates, objectGroup, declaredLandTypes,
            item.SourcePublishedAt, item.Latitude, item.Longitude, contacts,
            HasLandTypeConflict(declaredLandTypes, inferredLandTypes));
    }

    public async Task<IReadOnlyList<IncomingDuplicateLinkTargetView>> SearchDuplicateTargetsAsync(
        Subject subject, Guid catalogItemId, string text, CancellationToken cancellationToken)
    {
        AccessContext context = (await RequireReadAsync(subject, cancellationToken)).OrganizationContext;
        string search = text.Trim();
        if (search.Length > 200) throw new ArgumentException("Поиск ограничен 200 символами.");

        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        Listing source = await db.Listings.AsNoTracking().SingleOrDefaultAsync(value =>
            value.Id == catalogItemId && value.OrganizationId == context.OrganizationId, cancellationToken)
            ?? throw new AccessDeniedException();

        IQueryable<Listing> query = db.Listings.AsNoTracking().Where(value =>
            value.OrganizationId == context.OrganizationId && value.Id != catalogItemId
            && value.Disposition != CatalogDisposition.Fake);
        if (source.ObjectGroupId is Guid sourceGroupId)
            query = query.Where(value => value.ObjectGroupId != sourceGroupId);
        if (search.Length > 0)
        {
            string pattern = $"%{search}%";
            query = query.Where(value => EF.Functions.ILike(value.Title ?? "", pattern)
                || EF.Functions.ILike(value.Location ?? "", pattern)
                || EF.Functions.ILike(value.ExternalId ?? "", pattern)
                || EF.Functions.ILike(value.CadastralNumber ?? "", pattern)
                || EF.Functions.ILike(value.SellerName ?? "", pattern));
        }

        Listing[] targets = await query.OrderByDescending(value => value.ChangedAt).ThenBy(value => value.Id)
            .Take(30).ToArrayAsync(cancellationToken);
        Guid[] ids = targets.Select(value => value.Id).ToArray();
        Guid[] groupIds = targets.Where(value => value.ObjectGroupId != null)
            .Select(value => value.ObjectGroupId!.Value).Distinct().ToArray();
        Dictionary<Guid, int> groupCounts = groupIds.Length == 0
            ? []
            : await db.Listings.AsNoTracking().Where(value => value.OrganizationId == context.OrganizationId
                    && value.ObjectGroupId != null && groupIds.Contains(value.ObjectGroupId.Value))
                .GroupBy(value => value.ObjectGroupId!.Value)
                .ToDictionaryAsync(group => group.Key, group => group.Count(), cancellationToken);
        var links = await (from link in db.PropertyCaseSourceLinks.AsNoTracking()
                           join propertyCase in db.PropertyCases.AsNoTracking() on link.PropertyCaseId equals propertyCase.Id
                           where ids.Contains(link.CatalogItemId) && link.Confirmed
                           select new { link.CatalogItemId, propertyCase.Id, propertyCase.BusinessNumber })
            .ToArrayAsync(cancellationToken);
        var linksByItem = links.ToDictionary(value => value.CatalogItemId);

        return targets.Select(value =>
        {
            linksByItem.TryGetValue(value.Id, out var link);
            int groupCount = value.ObjectGroupId is Guid groupId && groupCounts.TryGetValue(groupId, out int count) ? count : 0;
            return new IncomingDuplicateLinkTargetView(value.Id, value.Source, value.Title ?? "Название неизвестно",
                value.Location, value.Price, PricePerSotka(value), value.AreaSquareMeters,
                value.CadastralNumber, value.ObjectGroupId, groupCount, link?.Id, link?.BusinessNumber);
        }).ToArray();
    }

    private static CatalogItemView CatalogView(Listing item, Guid? caseId, string? businessNumber, string? caseStage,
        int objectGroupMemberCount = 0) => new(
        item.Id, item.Source, item.ExternalId, item.Url, item.Title ?? "Название неизвестно", item.Price,
        PricePerSotka(item), item.Currency, item.AreaSquareMeters, item.Location,
        item.CadastralNumber, item.Description, item.Provenance, item.IngestionKind, item.Disposition,
        item.QueueReason, item.AttentionRequired, item.ReceivedAt, item.ChangedAt, item.LastObservedAt,
        caseId, businessNumber, caseStage, caseStage is "rejected" or "monitor", item.Version,
        item.ObjectGroupId, objectGroupMemberCount, item.IncludeInCalculation, CatalogCalculationEligibility.Reason(item));

    private static int Completeness(Listing item)
    {
        int known = 0;
        if (item.Price != null) known++;
        if (item.AreaSquareMeters != null) known++;
        if (!string.IsNullOrWhiteSpace(item.Location)) known++;
        if (!string.IsNullOrWhiteSpace(item.CadastralNumber)) known++;
        if (!string.IsNullOrWhiteSpace(item.Description)) known++;
        return known * 20;
    }

    private static IncomingCatalogRowState RowState(Listing item, bool priceChanged, bool returned) =>
        item.Price == null || item.AreaSquareMeters == null || string.IsNullOrWhiteSpace(item.Location)
            ? IncomingCatalogRowState.Incomplete
            : returned ? IncomingCatalogRowState.ReturnedFromMonitoring
            : priceChanged ? IncomingCatalogRowState.PriceChanged
            : IncomingCatalogRowState.Normal;

    private static IncomingLandType[] DeclaredLandTypes(Listing item) => (item.DeclaredLandTypes ?? [])
        .Select(value => Enum.TryParse(value, ignoreCase: true, out IncomingLandType type) ? (IncomingLandType?)type : null)
        .Where(value => value.HasValue).Select(value => value!.Value).Distinct().Order().ToArray();

    private static bool HasLandTypeConflict(IncomingLandType[] declared, IncomingLandType[] inferred) =>
        declared.Length > 0 && inferred.Length > 0
        && !declared.ToHashSet().SetEquals(inferred);

    private static string[] DuplicateReasons(string json)
    {
        try { return JsonSerializer.Deserialize<string[]>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    private static string[] PhotoUrls(string json)
    {
        try
        {
            return (JsonSerializer.Deserialize<string[]>(json) ?? [])
                .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    internal static string[] SearchTerms(string text) => text.Trim()
        .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray();

    private static (IncomingCatalogMatchField? Field, string? Value) SearchMatch(Listing item, string[] terms)
    {
        if (terms.Length == 0 || string.IsNullOrWhiteSpace(item.SellerName)) return (null, null);
        foreach (string term in terms)
        {
            bool visible = Contains(item.Title, term) || Contains(item.Location, term)
                || Contains(item.ExternalId, term) || Contains(item.CadastralNumber, term);
            if (!visible && Contains(item.SellerName, term))
                return (IncomingCatalogMatchField.SellerName, item.SellerName);
        }
        return (null, null);
    }

    private static bool Contains(string? value, string term) => value?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;

    // One safe value expression serves SQL ordering and the existing displayed price semantics.
    // CASE guards division; rounding remains presentation-only as before, not a sorting tie-breaker.
    private static readonly Expression<Func<Listing, decimal?>> PricePerSotkaKey = item =>
        item.Price > 0 && item.AreaSquareMeters > 0 ? item.Price.Value * 100m / item.AreaSquareMeters.Value : null;
    private static readonly Expression<Func<Listing, bool>> UnknownPricePerSotka = Expression.Lambda<Func<Listing, bool>>(
        Expression.Equal(PricePerSotkaKey.Body, Expression.Constant(null, typeof(decimal?))), PricePerSotkaKey.Parameters);
    private static readonly Func<Listing, decimal?> ReadPricePerSotka = PricePerSotkaKey.Compile();
    private static decimal? PricePerSotka(Listing item) => ReadPricePerSotka(item) is decimal value
        ? decimal.Round(value, 4, MidpointRounding.ToEven) : null;

    internal static (DateTimeOffset Start, DateTimeOffset End) BusinessDayUtc(DateTimeOffset instant)
    {
        TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(DataConventions.BusinessTimeZoneId);
        DateTime localDate = TimeZoneInfo.ConvertTime(instant, zone).Date;
        DateTime startUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localDate, DateTimeKind.Unspecified), zone);
        DateTime endUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localDate.AddDays(1), DateTimeKind.Unspecified), zone);
        return (new DateTimeOffset(startUtc), new DateTimeOffset(endUtc));
    }

    internal static void Validate(IncomingCatalogReadFilter filter)
    {
        IncomingCatalogFilter baseFilter = filter.Base;
        if (baseFilter.Text.Length > 200 || baseFilter.Offset < 0 || baseFilter.Size is < 1 or > 100 || !Enum.IsDefined(baseFilter.Age)
            || !Enum.IsDefined(filter.SortField) || !Enum.IsDefined(filter.SortDirection)
            || baseFilter.MinPrice < 0 || baseFilter.MaxPrice < 0 || baseFilter.MinAreaSquareMeters < 0 || baseFilter.MaxAreaSquareMeters < 0
            || filter.MinPricePerSotka < 0 || filter.MaxPricePerSotka < 0
            || baseFilter.MinPrice > baseFilter.MaxPrice || baseFilter.MinAreaSquareMeters > baseFilter.MaxAreaSquareMeters
            || filter.MinPricePerSotka > filter.MaxPricePerSotka
            || (filter.LandTypes?.Any(value => !Enum.IsDefined(value)) ?? false))
            throw new ArgumentException("Некорректный диапазон фильтра входящих.");
    }
}
