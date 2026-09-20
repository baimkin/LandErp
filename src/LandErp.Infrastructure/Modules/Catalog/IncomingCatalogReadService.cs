using LandErp.Application.Foundation;
using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace LandErp.Infrastructure.Modules.Catalog;

/// <summary>
/// Read-only projections for Incoming V2. Derives working views from facts already persisted by Catalog/Collection;
/// it deliberately does not own commands or introduce Listing persistence fields.
/// </summary>
public sealed class IncomingCatalogReadService(
    IDbContextFactory<LandErpDbContext> factory,
    IAccessControl access,
    ICatalogWorkspace catalogWorkspace,
    TimeProvider time) : IIncomingCatalogReadService
{
    public async Task<IncomingCatalogReadPage> ReadAsync(Subject subject, IncomingCatalogReadFilter filter, CancellationToken cancellationToken)
    {
        AccessContext context = await access.RequireAsync(subject, Permissions.QueueRead, cancellationToken);
        IncomingCatalogFilter baseFilter = filter.Base;
        Validate(filter);

        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        IQueryable<Listing> organizationItems = db.Listings.AsNoTracking()
            .Where(item => item.OrganizationId == context.OrganizationId);
        IQueryable<Listing> incomingItems = organizationItems.Where(item => item.Disposition == CatalogDisposition.Incoming);
        DateTimeOffset now = time.GetUtcNow();
        (DateTimeOffset businessDayStart, DateTimeOffset businessDayEnd) = BusinessDayUtc(now);

        IQueryable<Guid> priceChangedIds = db.CatalogEvents.AsNoTracking()
            .Where(item => item.OrganizationId == context.OrganizationId
                && item.Kind == CatalogEventKind.SourceChanged && item.Message.Contains("цена"))
            .Select(item => item.CatalogItemId).Distinct();
        IQueryable<Guid> returnedFromMonitoringIds = db.CatalogEvents.AsNoTracking()
            .Where(item => item.OrganizationId == context.OrganizationId && item.Kind == CatalogEventKind.MonitoringTriggered)
            .Select(item => item.CatalogItemId).Distinct();
        IQueryable<Guid> reviewedIds = db.CatalogEvents.AsNoTracking()
            .Where(item => item.OrganizationId == context.OrganizationId
                && (item.Kind == CatalogEventKind.ReviewStarted
                    || item.Kind == CatalogEventKind.Classified
                    || item.Kind == CatalogEventKind.MonitoringStarted
                    || item.Kind == CatalogEventKind.CaseResumed))
            .Select(item => item.CatalogItemId)
            .Union(db.PropertyCaseSourceLinks.AsNoTracking()
                .Where(item => item.OrganizationId == context.OrganizationId && item.Confirmed)
                .Select(item => item.CatalogItemId))
            .Distinct();
        IQueryable<Guid> processedTodayIds = db.CatalogEvents.AsNoTracking()
            .Where(item => item.OrganizationId == context.OrganizationId
                && item.RecordedAt >= businessDayStart && item.RecordedAt < businessDayEnd
                && (item.Kind == CatalogEventKind.Classified
                    || item.Kind == CatalogEventKind.MonitoringStarted
                    || item.Kind == CatalogEventKind.CaseResumed))
            .Select(item => item.CatalogItemId)
            .Union(db.PropertyCaseSourceLinks.AsNoTracking()
                .Where(item => item.OrganizationId == context.OrganizationId && item.Confirmed
                    && item.RecordedAt >= businessDayStart && item.RecordedAt < businessDayEnd)
                .Select(item => item.CatalogItemId))
            .Distinct();

        IncomingCatalogReadSummary summary = new(
            await incomingItems.CountAsync(cancellationToken),
            await organizationItems.CountAsync(item => item.AttentionRequired, cancellationToken),
            await organizationItems.CountAsync(item => item.Disposition == CatalogDisposition.Monitoring, cancellationToken),
            await organizationItems.CountAsync(item => item.Disposition == CatalogDisposition.InWork, cancellationToken),
            await incomingItems.CountAsync(item => item.Price == null || item.AreaSquareMeters == null || item.Location == null, cancellationToken),
            await incomingItems.CountAsync(item => priceChangedIds.Contains(item.Id), cancellationToken),
            await incomingItems.CountAsync(item => returnedFromMonitoringIds.Contains(item.Id), cancellationToken),
            New: await incomingItems.CountAsync(item => !reviewedIds.Contains(item.Id), cancellationToken),
            ProcessedToday: await processedTodayIds.CountAsync(cancellationToken));

        IQueryable<Listing> query = organizationItems;
        string[] searchTerms = SearchTerms(baseFilter.Text);
        foreach (string term in searchTerms)
        {
            string pattern = $"%{term}%";
            query = query.Where(item =>
                EF.Functions.ILike(item.Title ?? "", pattern)
                || EF.Functions.ILike(item.Location ?? "", pattern)
                || EF.Functions.ILike(item.ExternalId ?? "", pattern)
                || EF.Functions.ILike(item.CadastralNumber ?? "", pattern)
                || EF.Functions.ILike(item.SellerName ?? "", pattern));
        }
        if (baseFilter.Source != null) query = query.Where(item => item.Source == baseFilter.Source);
        if (baseFilter.Disposition != null) query = query.Where(item => item.Disposition == baseFilter.Disposition);
        if (baseFilter.AttentionOnly) query = query.Where(item => item.AttentionRequired);
        if (baseFilter.MinPrice != null) query = query.Where(item => item.Price >= baseFilter.MinPrice);
        if (baseFilter.MaxPrice != null) query = query.Where(item => item.Price <= baseFilter.MaxPrice);
        if (baseFilter.MinAreaSquareMeters != null) query = query.Where(item => item.AreaSquareMeters >= baseFilter.MinAreaSquareMeters);
        if (baseFilter.MaxAreaSquareMeters != null) query = query.Where(item => item.AreaSquareMeters <= baseFilter.MaxAreaSquareMeters);
        if (filter.MinPricePerSotka != null)
            query = query.Where(item => item.Price != null && item.AreaSquareMeters > 0
                && item.Price.Value * 100m / item.AreaSquareMeters.Value >= filter.MinPricePerSotka.Value);
        if (filter.MaxPricePerSotka != null)
            query = query.Where(item => item.Price != null && item.AreaSquareMeters > 0
                && item.Price.Value * 100m / item.AreaSquareMeters.Value <= filter.MaxPricePerSotka.Value);
        query = IncomingLandTypeClassifier.ApplyFilter(query, filter.LandTypes);

        query = baseFilter.Age switch
        {
            CatalogAgeRange.Today => query.Where(item => item.ReceivedAt >= now.AddDays(-1)),
            CatalogAgeRange.ThreeDays => query.Where(item => item.ReceivedAt >= now.AddDays(-3)),
            CatalogAgeRange.Week => query.Where(item => item.ReceivedAt >= now.AddDays(-7)),
            CatalogAgeRange.OlderThanWeek => query.Where(item => item.ReceivedAt < now.AddDays(-7)),
            _ => query
        };

        if (filter.SearchGroupId != null)
        {
            Guid groupId = filter.SearchGroupId.Value;
            IQueryable<Guid> groupListingIds =
                from observation in db.ListingObservations.AsNoTracking()
                join job in db.CollectionJobs.AsNoTracking() on observation.JobId equals job.Id
                join search in db.SearchConfigurations.AsNoTracking() on job.SearchId equals search.Id
                join searchGroup in db.SearchGroups.AsNoTracking() on search.SearchGroupId equals searchGroup.Id
                where job.OrganizationId == context.OrganizationId && search.OrganizationId == context.OrganizationId
                    && searchGroup.OrganizationId == context.OrganizationId && searchGroup.Active && searchGroup.Id == groupId
                select observation.ListingId;
            query = query.Where(item => groupListingIds.Contains(item.Id));
        }

        if (filter.SearchConfigurationId != null)
        {
            Guid searchId = filter.SearchConfigurationId.Value;
            IQueryable<Guid> searchListingIds =
                from observation in db.ListingObservations.AsNoTracking()
                join job in db.CollectionJobs.AsNoTracking() on observation.JobId equals job.Id
                join search in db.SearchConfigurations.AsNoTracking() on job.SearchId equals search.Id
                where job.OrganizationId == context.OrganizationId && search.OrganizationId == context.OrganizationId
                    && search.Id == searchId
                select observation.ListingId;
            query = query.Where(item => searchListingIds.Contains(item.Id));
        }

        query = filter.Preset switch
        {
            IncomingCatalogPreset.New => query.Where(item => !reviewedIds.Contains(item.Id)),
            IncomingCatalogPreset.PriceChanged => query.Where(item => priceChangedIds.Contains(item.Id)),
            IncomingCatalogPreset.Incomplete => query.Where(item => item.Price == null || item.AreaSquareMeters == null || item.Location == null),
            IncomingCatalogPreset.ReturnedFromMonitoring => query.Where(item => returnedFromMonitoringIds.Contains(item.Id)),
            IncomingCatalogPreset.ProcessedToday => query.Where(item => processedTodayIds.Contains(item.Id)),
            _ => query
        };

        int total = await query.CountAsync(cancellationToken);
        IOrderedQueryable<Listing> ordered = (filter.SortField, filter.SortDirection) switch
        {
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
        Listing[] items = await ordered.Skip(baseFilter.Offset).Take(baseFilter.Size).ToArrayAsync(cancellationToken);
        Guid[] ids = items.Select(item => item.Id).ToArray();

        var linked = await (from link in db.PropertyCaseSourceLinks.AsNoTracking()
                            join propertyCase in db.PropertyCases.AsNoTracking() on link.PropertyCaseId equals propertyCase.Id
                            where ids.Contains(link.CatalogItemId) && link.Confirmed
                            select new { link.CatalogItemId, propertyCase.Id, propertyCase.BusinessNumber, propertyCase.StageId })
            .ToArrayAsync(cancellationToken);
        var linksByItem = linked.ToDictionary(item => item.CatalogItemId);

        var searchRows = await (from observation in db.ListingObservations.AsNoTracking()
                                join job in db.CollectionJobs.AsNoTracking() on observation.JobId equals job.Id
                                join search in db.SearchConfigurations.AsNoTracking() on job.SearchId equals search.Id
                                where ids.Contains(observation.ListingId) && search.OrganizationId == context.OrganizationId
                                select new { observation.ListingId, observation.ObservedAt, SearchId = search.Id, search.Label })
            .ToArrayAsync(cancellationToken);
        var searchByItem = searchRows.GroupBy(item => item.ListingId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.ObservedAt).First());

        HashSet<Guid> reviewedOnPage = (await reviewedIds.Where(id => ids.Contains(id)).ToArrayAsync(cancellationToken)).ToHashSet();

        var eventRows = await db.CatalogEvents.AsNoTracking()
            .Where(item => ids.Contains(item.CatalogItemId)
                && (item.Kind == CatalogEventKind.SourceChanged || item.Kind == CatalogEventKind.MonitoringTriggered))
            .Select(item => new { item.CatalogItemId, item.Kind, item.Message }).ToArrayAsync(cancellationToken);
        var flagsByItem = eventRows.GroupBy(item => item.CatalogItemId).ToDictionary(group => group.Key, group => new
        {
            PriceChanged = group.Any(item => item.Kind == CatalogEventKind.SourceChanged && item.Message.Contains("цена")),
            Returned = group.Any(item => item.Kind == CatalogEventKind.MonitoringTriggered)
        });

        Dictionary<Guid, IncomingCatalogRowRead> rows = [];
        CatalogItemView[] views = items.Select(item =>
        {
            linksByItem.TryGetValue(item.Id, out var link);
            searchByItem.TryGetValue(item.Id, out var search);
            flagsByItem.TryGetValue(item.Id, out var flags);
            bool priceChanged = flags?.PriceChanged ?? false;
            bool returned = flags?.Returned ?? false;
            string[] photos = PhotoUrls(item.PhotosJson);
            IncomingLandType[] landTypes = IncomingLandTypeClassifier.Classify(item.Title, item.Description);
            (IncomingCatalogMatchField? matchedField, string? matchedValue) = SearchMatch(item, searchTerms);
            rows[item.Id] = new(item.Id, search?.SearchId, search?.Label, Completeness(item),
                RowState(item, priceChanged, returned), priceChanged, returned, photos.FirstOrDefault(), photos.Length,
                landTypes, matchedField, matchedValue, Reviewed: reviewedOnPage.Contains(item.Id));
            return CatalogView(item, link?.Id, link?.BusinessNumber, link?.StageId);
        }).ToArray();

        IncomingSearchGroupView[] groups = await db.SearchGroups.AsNoTracking()
            .Where(item => item.OrganizationId == context.OrganizationId && item.Active)
            .OrderBy(item => item.SortOrder).ThenBy(item => item.Name).ThenBy(item => item.Id)
            .Select(item => new IncomingSearchGroupView(item.Id, item.Name, item.SortOrder))
            .ToArrayAsync(cancellationToken);
        IncomingSearchConfigurationView[] searches = await db.SearchConfigurations.AsNoTracking()
            .Where(item => item.OrganizationId == context.OrganizationId && item.Enabled)
            .OrderBy(item => item.Label).ThenBy(item => item.Id)
            .Select(item => new IncomingSearchConfigurationView(item.Id, item.Label, item.Source, item.SearchGroupId))
            .ToArrayAsync(cancellationToken);

        return new(views, total, summary, groups, searches, rows);
    }

    public async Task<IncomingCatalogDetailRead> ReadDetailAsync(Subject subject, Guid catalogItemId, CancellationToken cancellationToken)
    {
        CatalogItemDetail detail = await catalogWorkspace.ReadItemAsync(subject, catalogItemId, cancellationToken);
        AccessContext context = await access.RequireAsync(subject, Permissions.QueueRead, cancellationToken);
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

        return new(detail, PhotoUrls(item.PhotosJson), search?.SearchId, search?.Label, Completeness(item),
            RowState(item, priceChanged, returned), priceChanged, returned,
            IncomingLandTypeClassifier.Classify(item.Title, item.Description));
    }

    private static CatalogItemView CatalogView(Listing item, Guid? caseId, string? businessNumber, string? caseStage) => new(
        item.Id, item.Source, item.ExternalId, item.Url, item.Title ?? "Название неизвестно", item.Price,
        PricePerSotka(item.Price, item.AreaSquareMeters), item.Currency, item.AreaSquareMeters, item.Location,
        item.CadastralNumber, item.Description, item.Provenance, item.IngestionKind, item.Disposition,
        item.QueueReason, item.AttentionRequired, item.ReceivedAt, item.ChangedAt, item.LastObservedAt,
        caseId, businessNumber, caseStage, caseStage is "rejected" or "monitor", item.Version);

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

    private static string[] SearchTerms(string text) => text.Trim()
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

    private static decimal? PricePerSotka(decimal? price, decimal? areaSquareMeters) => price is > 0 && areaSquareMeters is > 0
        ? decimal.Round(price.Value * 100m / areaSquareMeters.Value, 4, MidpointRounding.ToEven) : null;

    private static (DateTimeOffset Start, DateTimeOffset End) BusinessDayUtc(DateTimeOffset instant)
    {
        TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(DataConventions.BusinessTimeZoneId);
        DateTime localDate = TimeZoneInfo.ConvertTime(instant, zone).Date;
        DateTime startUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localDate, DateTimeKind.Unspecified), zone);
        DateTime endUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localDate.AddDays(1), DateTimeKind.Unspecified), zone);
        return (new DateTimeOffset(startUtc), new DateTimeOffset(endUtc));
    }

    private static void Validate(IncomingCatalogReadFilter filter)
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
