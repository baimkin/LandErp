using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Modules.Catalog;

internal static class CatalogMarketParticipants
{
    internal sealed record Participant(Guid GroupId, Guid ListingId, Guid? ObjectGroupId,
        decimal Price, decimal Area, DateTimeOffset? LastGroupObservation)
    {
        public decimal PricePerSotka => Price * 100m / Area;
    }

    internal static async Task<Participant[]> ReadAsync(LandErpDbContext db, Guid organizationId,
        Guid[] groupIds, CancellationToken cancellationToken)
    {
        if (groupIds.Length == 0) return [];
        // Filter before choosing the representative; an ineligible/unmarked fresher source
        // must never displace an eligible one. No old market settings participate here.
        var eligible = db.Listings.AsNoTracking().Where(item => item.OrganizationId == organizationId
            && item.IncludeInCalculation).Where(CatalogCalculationEligibility.Eligible);
        var candidates = await (from listing in eligible
            join observation in db.ListingObservations.AsNoTracking() on listing.Id equals observation.ListingId
            join job in db.CollectionJobs.AsNoTracking() on observation.JobId equals job.Id
            join search in db.SearchConfigurations.AsNoTracking() on job.SearchId equals search.Id
            join groupItem in db.SearchGroups.AsNoTracking() on search.SearchGroupId equals groupItem.Id
            where job.OrganizationId == organizationId && search.OrganizationId == organizationId
                && groupItem.OrganizationId == organizationId && groupItem.Active && groupIds.Contains(groupItem.Id)
            group observation by new { GroupId = groupItem.Id, listing.Id, listing.ObjectGroupId, listing.Price, listing.AreaSquareMeters } into observations
            select new Participant(observations.Key.GroupId, observations.Key.Id, observations.Key.ObjectGroupId,
                observations.Key.Price!.Value, observations.Key.AreaSquareMeters!.Value,
                observations.Max(item => (DateTimeOffset?)item.ObservedAt))).ToArrayAsync(cancellationToken);
        // SQL above collapses repeated observations/searches. Only these scalar candidates are
        // materialized; the same result feeds market prices, count, and the Incoming participant mode.
        return candidates.GroupBy(item => (item.GroupId, IsObjectGroup: item.ObjectGroupId != null,
                Identity: item.ObjectGroupId ?? item.ListingId))
            .Select(group => group.OrderByDescending(item => item.LastGroupObservation.HasValue)
                .ThenByDescending(item => item.LastGroupObservation).ThenBy(item => item.ListingId).First()).ToArray();
    }

    internal static decimal Round(decimal value) => decimal.Round(value, 4, MidpointRounding.ToEven);
}
