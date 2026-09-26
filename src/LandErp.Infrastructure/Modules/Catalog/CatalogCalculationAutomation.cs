using System.Linq.Expressions;
using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LandErp.Infrastructure.Modules.Catalog;

internal static class CatalogCalculationAutomation
{
    // Caller persists new items and observations inside its uncommitted ingress transaction first.
    // Only IDs actually created by that ingress call may enter here; reobservations never do.
    internal static async Task IncludeNewAsync(LandErpDbContext db, Guid organizationId,
        IReadOnlyCollection<Guid> newIds, Guid? actorId, string correlationId, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (newIds.Count == 0) return;
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var saved = await IncomingFilterPresetService.ReadForOrganizationAsync(connection, organizationId, cancellationToken);
        var automatic = saved.Where(item => item.CompatibilityIssue == null && item.Criteria.AutoIncludeNewInCalculation).ToArray();
        if (automatic.Length == 0) return;
        var prepared = await IncomingCatalogSelection.PrepareAsync(db, organizationId, now,
            new(new(), WorkingScope: new(IncomingCatalogMode.AllListings)), saved, cancellationToken);
        Expression<Func<Listing, bool>> matches = item => false;
        foreach (var preset in automatic)
            matches = IncomingCatalogQuery.Or(matches,
                prepared.Predicates.Conditions(IncomingCatalogQuery.FromCriteria(preset.Criteria)));
        Guid[] matchedIds = await db.Listings.Where(item => item.OrganizationId == organizationId && newIds.Contains(item.Id))
            .Where(matches).Select(item => item.Id).ToArrayAsync(cancellationToken);
        foreach (Guid id in matchedIds)
        {
            // Read predicates contain AsNoTracking subqueries. Mutate the original ingress aggregate,
            // not an entity materialized by the read path; it must still belong to this transaction.
            Listing item = db.Listings.Local.Single(item => item.Id == id && item.OrganizationId == organizationId);
            if (CatalogCalculationEligibility.Reason(item) == null)
                CatalogCalculationService.Change(db, item, true, actorId, "Новое объявление подходит под автоматический фильтр",
                    correlationId, now);
        }
    }
}
