using System.Text.Json;
using LandErp.Application.Foundation;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Collector.Contracts.V1;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Modules.Procurement;

public sealed partial class ProcurementQueueV2ReadService
{
    private sealed record RevisionCandidate(Guid CaseId, Guid CatalogItemId, long DataRevision, long ReviewedDataRevision);

    private static async Task<decimal?> ReadStartPriceAsync(LandErpDbContext db, PropertyCase propertyCase, SourceDb[] sources,
        CancellationToken cancellationToken)
    {
        SourceDb? baselineSource = sources.OrderBy(item => item.LinkRecordedAt).ThenBy(item => item.CatalogItemId).FirstOrDefault();
        if (baselineSource == null) return propertyCase.WorkingPrice;

        CatalogObservation? observation = await db.ListingObservations.AsNoTracking()
            .Where(item => item.ListingId == baselineSource.CatalogItemId && item.RecordedAt <= baselineSource.LinkRecordedAt)
            .OrderByDescending(item => item.RecordedAt).ThenByDescending(item => item.ObservedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (observation != null)
        {
            try
            {
                ListingData? data = JsonSerializer.Deserialize<ListingData>(observation.PayloadJson, CollectionJson.Options);
                if (data?.Price.Presence == FieldPresence.Present && data.Price.Parsed != null)
                    return DataConventions.RoundRubles(data.Price.Parsed.Value);
            }
            catch (JsonException)
            {
            }
        }

        return propertyCase.WorkingPrice ?? baselineSource.Price;
    }
}
