using System.Linq.Expressions;
using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Modules.Catalog;

/// <summary>One predicate path for page rows, saved-filter OR and conditional SQL counts. No listing materialization.</summary>
internal sealed class IncomingCatalogQuery(LandErpDbContext db, Guid organizationId, DateTimeOffset now,
    IQueryable<Guid> priceChangedIds, IQueryable<Guid> returnedFromMonitoringIds, IQueryable<Guid> reviewedIds,
    IQueryable<Guid> pendingDuplicateListingIds, IQueryable<Guid> processedTodayIds)
{
    public Expression<Func<Listing, bool>> Conditions(IncomingCatalogReadFilter filter)
    {
        IncomingCatalogFilter baseFilter = filter.Base;
        IQueryable<Listing> query = Array.Empty<Listing>().AsQueryable();
        string[] searchTerms = IncomingCatalogReadService.SearchTerms(baseFilter.Text);
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
        if (baseFilter.Disposition != null && filter.Preset != IncomingCatalogPreset.PossibleDuplicate)
            query = query.Where(item => item.Disposition == baseFilter.Disposition);
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
                where job.OrganizationId == organizationId && search.OrganizationId == organizationId
                    && searchGroup.OrganizationId == organizationId && searchGroup.Active && searchGroup.Id == groupId
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
                where job.OrganizationId == organizationId && search.OrganizationId == organizationId
                    && search.Id == searchId
                select observation.ListingId;
            query = query.Where(item => searchListingIds.Contains(item.Id));
        }

        query = filter.Preset switch
        {
            IncomingCatalogPreset.New => query.Where(item => !reviewedIds.Contains(item.Id)),
            IncomingCatalogPreset.PriceChanged => query.Where(item => priceChangedIds.Contains(item.Id)),
            IncomingCatalogPreset.PossibleDuplicate => query.Where(item =>
                (item.Disposition == CatalogDisposition.Incoming || item.Disposition == CatalogDisposition.Duplicate)
                && pendingDuplicateListingIds.Contains(item.Id)),
            IncomingCatalogPreset.Incomplete => query.Where(item => item.Price == null || item.AreaSquareMeters == null || item.Location == null),
            IncomingCatalogPreset.ReturnedFromMonitoring => query.Where(item => returnedFromMonitoringIds.Contains(item.Id)),
            IncomingCatalogPreset.ProcessedToday => query.Where(item => processedTodayIds.Contains(item.Id)),
            _ => query
        };

        // Collect only the Where chain assembled above. The empty seed is never executed;
        // provider-backed observation/event subqueries inside predicates remain SQL expressions.
        Expression<Func<Listing, bool>> predicate = item => true;
        Expression expression = query.Expression;
        while (expression is MethodCallExpression call && call.Method.Name == nameof(Queryable.Where))
        {
            var condition = (Expression<Func<Listing, bool>>)((UnaryExpression)call.Arguments[1]).Operand;
            predicate = And(predicate, condition);
            expression = call.Arguments[0];
        }
        return predicate;
    }

    public Expression<Func<Listing, bool>> Slice(IncomingCatalogPreset? slice) => slice switch
    {
        IncomingCatalogPreset.New => item => item.Disposition == CatalogDisposition.Incoming && !reviewedIds.Contains(item.Id),
        IncomingCatalogPreset.PriceChanged => item => item.Disposition == CatalogDisposition.Incoming && priceChangedIds.Contains(item.Id),
        IncomingCatalogPreset.Incomplete => item => item.Disposition == CatalogDisposition.Incoming
            && (item.Price == null || item.AreaSquareMeters == null || item.Location == null),
        IncomingCatalogPreset.ReturnedFromMonitoring => item => item.Disposition == CatalogDisposition.Incoming && returnedFromMonitoringIds.Contains(item.Id),
        IncomingCatalogPreset.PossibleDuplicate => item =>
            (item.Disposition == CatalogDisposition.Incoming || item.Disposition == CatalogDisposition.Duplicate)
            && pendingDuplicateListingIds.Contains(item.Id),
        IncomingCatalogPreset.ProcessedToday => item => processedTodayIds.Contains(item.Id),
        _ => item => true
    };

    public static IncomingCatalogReadFilter FromCriteria(IncomingFilterPresetCriteriaV1 c, string text = "") => new(
        new(text, c.Source, c.Disposition, c.Age, c.MinTotalPrice, c.MaxTotalPrice,
            c.MinAreaSquareMeters, c.MaxAreaSquareMeters, c.AttentionOnly),
        c.SearchConfigurationId, c.Preset, c.SortField, c.SortDirection, c.SearchGroupId,
        c.MinPricePerSotka, c.MaxPricePerSotka, c.LandTypes);

    public static Expression<Func<Listing, bool>> And(Expression<Func<Listing, bool>> left, Expression<Func<Listing, bool>> right)
        => Combine(left, right, false);
    public static Expression<Func<Listing, bool>> Or(Expression<Func<Listing, bool>> left, Expression<Func<Listing, bool>> right)
        => Combine(left, right, true);
    private static Expression<Func<Listing, bool>> Combine(Expression<Func<Listing, bool>> left,
        Expression<Func<Listing, bool>> right, bool or)
    {
        Expression body = new ReplaceParameter(right.Parameters[0], left.Parameters[0]).Visit(right.Body);
        return Expression.Lambda<Func<Listing, bool>>(or ? Expression.OrElse(left.Body, body)
            : Expression.AndAlso(left.Body, body), left.Parameters);
    }
    private sealed class ReplaceParameter(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }

    public static async Task<int[]> CountAsync(IQueryable<Listing> organizationItems,
        IReadOnlyList<Expression<Func<Listing, bool>>> predicates, CancellationToken cancellationToken)
    {
        ParameterExpression group = Expression.Parameter(typeof(IGrouping<int, Listing>), "items");
        var counts = predicates.Select(predicate => Expression.Call(typeof(Enumerable), nameof(Enumerable.Count),
            [typeof(Listing)], group, predicate));
        var projection = Expression.Lambda<Func<IGrouping<int, Listing>, int[]>>(Expression.NewArrayInit(typeof(int), counts), group);
        // All alternative filters and slices are conditional aggregates in one SQL query,
        // not separate scans/round trips or counts over the materialized page.
        return await organizationItems.GroupBy(item => 1).Select(projection).SingleOrDefaultAsync(cancellationToken)
            ?? new int[predicates.Count];
    }
}