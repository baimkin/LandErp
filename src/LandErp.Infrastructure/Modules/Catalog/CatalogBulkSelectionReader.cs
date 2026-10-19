using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Modules.Catalog;

internal static class CatalogBulkSelectionReader
{
    internal static async Task<Listing[]> ReadAsync(LandErpDbContext db,
        IIncomingFilterPresetService presets, Subject subject, Guid organizationId, DateTimeOffset now,
        CatalogBulkSelection selection, CancellationToken cancellationToken)
    {
        var saved = selection.Filter.WorkingScope == null || selection.Filter.WorkingScope.Mode
            is IncomingCatalogMode.Archive or IncomingCatalogMode.Participants
            ? Array.Empty<IncomingFilterPresetView>() : await presets.ReadAsync(subject, cancellationToken);
        var prepared = await IncomingCatalogSelection.PrepareAsync(db, organizationId, now,
            selection.Filter, saved, cancellationToken);
        // Use the list's full server predicate; pagination never limits a bulk action.
        var query = db.Listings.AsNoTracking().Where(item => item.OrganizationId == organizationId)
            .Where(prepared.Current);
        if (selection.Selected is { } targets)
        {
            if (selection.ExcludedIds is { Count: > 0 })
                throw new ArgumentException("Исключения доступны только при выборе всех результатов.");
            Guid[] ids = targets.Select(item => item.Id).Distinct().ToArray();
            if (ids.Length != targets.Count) throw new ArgumentException("Объявление выбрано несколько раз.");
            var items = await query.Where(item => ids.Contains(item.Id)).ToArrayAsync(cancellationToken);
            if (items.Length != targets.Count) throw Conflict();
            var versions = targets.ToDictionary(item => item.Id, item => item.ExpectedVersion);
            if (items.Any(item => item.Version != versions[item.Id])) throw Conflict();
            return items;
        }
        if (selection.ExcludedIds is { Count: > 0 } excluded)
            query = query.Where(item => !excluded.Contains(item.Id));
        return await query.ToArrayAsync(cancellationToken);
    }

    internal static DbUpdateConcurrencyException Conflict() => new(
        "Объявления или состав отбора изменились. Обновите список и проверьте количество заново.");
}
