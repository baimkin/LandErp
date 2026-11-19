using LandErp.Application.Modules.Collection.Domain;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Modules.Collection;

internal static class CollectionSearchAvailability
{
    // Enabled belongs to a search; archive belongs to its group. Both must allow a new run.
    // An ungrouped search is valid; a missing/foreign group fails closed.
    public static Task<bool> HasActiveGroupAsync(LandErpDbContext db, SearchConfiguration search, CancellationToken cancellationToken)
        => search.SearchGroupId is null ? Task.FromResult(true)
            : db.SearchGroups.AnyAsync(g => g.Id == search.SearchGroupId && g.OrganizationId == search.OrganizationId && g.Active, cancellationToken);
}
