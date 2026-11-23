namespace LandErp.ParserSpike.LocalCollection;

/// <summary>A run may collect only points in its saved area. Page URLs cannot replace that area.</summary>
public sealed class SearchAreaGuard(SearchAreaStore store, string url, bool server, string jobId)
{
    private SavedSearchArea? reference = store.Load(url, server);
    private readonly Dictionary<string, ListingObservation> pending = new(StringComparer.Ordinal);
    private SearchAreaEvidence? evidence;
    public SearchAreaCheck? LastCheck => evidence?.Check;

    public PageObservation Apply(PageObservation snapshot, string currentUrl)
    {
        if (snapshot.Kind != PageKind.SearchResults) return snapshot;
        bool filters;
        try { filters = AvitoSearchIdentity.SameFilters(url, currentUrl, snapshot.AvitoNeutralFormConfirmed); }
        catch (ArgumentException) { filters = false; }
        MapScope? sourceScope = snapshot.Map ?? snapshot.AreaScope;
        GeoPoint[][][] observed = sourceScope?.ObservedAreas ?? (sourceScope is { Rings.Length: > 0 } scope ? [scope.Rings] : []);
        string? signature = SearchAreaGeometry.Signature(observed);
        bool selection = snapshot.Map is null || sourceScope?.Selection?.Active == true;
        string? requestedDraw = AvitoMapData.DrawId(url), currentDraw = AvitoMapData.DrawId(currentUrl);
        // Owner-approved policy: Avito restores the selected area for the opened search.
        // Trust its first valid selected-area geometry after loading, never the viewport or drawId alone.
        // Existing references remain immutable; filters and membership are checked independently.
        if (reference is null && filters && signature is not null && selection && !snapshot.Loading) reference = store.Remember(url, server, observed);
        bool geometryMatches = reference is not null && signature == reference.Signature && selection && !snapshot.Loading;
        ListingObservation[] raw = snapshot.UnverifiedListings ?? snapshot.Listings;
        List<ListingObservation> accepted = [];
        HashSet<string> excluded = (snapshot.ExcludedExternalIds ?? []).ToHashSet(StringComparer.Ordinal);
        foreach (string id in excluded) pending.Remove(id);
        int outside = 0;
        bool truncated = false;
        foreach (ListingObservation item in raw)
        {
            if (excluded.Contains(item.ExternalId)) continue;
            if (!filters) continue;
            if (reference is null || !geometryMatches || item.Latitude.Parsed is null || item.Longitude.Parsed is null
                || item.Latitude.Parsed is < -90 or > 90 || item.Longitude.Parsed is < -180 or > 180)
            {
                if (pending.ContainsKey(item.ExternalId) || pending.Count < 5000) pending[item.ExternalId] = item; else truncated = true;
                continue;
            }
            pending.Remove(item.ExternalId);
            if (SearchAreaGeometry.Contains(reference.Polygons, item)) accepted.Add(item); else outside++;
        }
        string? problem = !filters ? "AREA_FILTERS_CHANGED" : signature is null ? (reference is null ? "AREA_REFERENCE_UNAVAILABLE" : "AREA_GEOMETRY_UNAVAILABLE")
            : !selection ? "AREA_SELECTION_UNCONFIRMED" : snapshot.Loading ? "AREA_LOADING"
            : reference is null ? "AREA_REFERENCE_UNAVAILABLE" : !geometryMatches ? "AREA_GEOMETRY_CHANGED"
            : pending.Count > 0 ? "AREA_COORDINATES_MISSING" : null;
        SearchAreaCheck check = new(filters, reference is not null, geometryMatches, problem,
            pending.Values.Count(item => item.Latitude.Parsed is null || item.Longitude.Parsed is null), outside, truncated);
        evidence = new(jobId, DateTimeOffset.UtcNow, check, reference?.Polygons ?? [], observed, requestedDraw, currentDraw);
        MapScope? map = snapshot.Map is null ? null : snapshot.Map with { ZoneConfirmed = geometryMatches && filters,
            Rings = reference?.Polygons[0] ?? snapshot.Map.Rings, ZoneError = problem };
        return snapshot with { Listings = accepted.ToArray(), Changes = accepted.ToArray(), Map = map,
            AreaCheck = check, SearchIdentityError = problem,
            Warnings = snapshot.Warnings.Concat(problem is null ? [] : new[] { problem }).Distinct(StringComparer.Ordinal).ToArray() };
    }

    public void Persist()
    {
        if (evidence is not null) store.WriteDiagnostics(url, server, evidence, pending.Values.ToArray());
    }
}
