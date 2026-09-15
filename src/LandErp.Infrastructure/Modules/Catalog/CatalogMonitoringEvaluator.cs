using LandErp.Application.Foundation;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Infrastructure.Persistence;

namespace LandErp.Infrastructure.Modules.Catalog;

internal static class CatalogMonitoringEvaluator
{
    internal static void Evaluate(LandErpDbContext db, Listing listing, DateTimeOffset now)
    {
        if (listing.Disposition != CatalogDisposition.Monitoring) return;

        decimal? perSotka = PricePerSotka(listing.Price, listing.AreaSquareMeters);
        listing.LastEvaluatedPrice = listing.Price;
        listing.LastEvaluatedPricePerSotka = perSotka;
        listing.LastEvaluatedAt = now;

        bool totalReached = listing.TargetTotalPrice != null && listing.Price != null
            && listing.Price <= listing.TargetTotalPrice;
        bool perSotkaReached = listing.TargetPricePerSotka != null && perSotka != null
            && perSotka <= listing.TargetPricePerSotka;
        if (!totalReached && !perSotkaReached) return;

        listing.Disposition = CatalogDisposition.Incoming;
        listing.AttentionRequired = true;
        listing.AttentionAt = now;
        listing.QueueReason = totalReached && perSotkaReached ? "Достигнуты оба порога мониторинга"
            : totalReached ? "Общая цена достигла порога мониторинга" : "Цена за сотку достигла порога мониторинга";
        db.CatalogEvents.Add(new()
        {
            Id = DataConventions.NewId(),
            OrganizationId = listing.OrganizationId,
            CatalogItemId = listing.Id,
            Kind = CatalogEventKind.MonitoringTriggered,
            Message = listing.QueueReason,
            ObservedPrice = listing.Price,
            ObservedPricePerSotka = perSotka,
            RecordedAt = now
        });
    }

    internal static decimal? PricePerSotka(decimal? price, decimal? areaSquareMeters) =>
        price is > 0 && areaSquareMeters is > 0
            ? decimal.Round(price.Value * 100m / areaSquareMeters.Value, 4, MidpointRounding.ToEven)
            : null;
}
