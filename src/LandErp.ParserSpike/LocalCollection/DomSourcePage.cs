using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using LandErp.ParserSpike.Contracts;
using Microsoft.Playwright;

namespace LandErp.ParserSpike.LocalCollection;

public sealed record DomCard(string Id, string Url, string? Title, string? Price, string? UnitPrice,
    string? Location, string? Transport, string? Description, string? Date, string? Seller,
    string? SellerUrl, string? SellerStatistics, string[] Badges, string[] Photos, string? SellerType = null,
    string? StructuredPrice = null, string? StructuredArea = null, string? StructuredAreaUnit = null,
    string? StructuredLocation = null, string? StructuredDescription = null, string[]? StructuredPhotos = null,
    string? CadastralNumber = null, long? SourcePublishedUnix = null, decimal? Latitude = null, decimal? Longitude = null,
    string[]? DeclaredLandTypes = null, string[]? LocationCandidates = null, bool? StructuredLocationHidden = null);
public sealed record DomSnapshot(string Kind, DomCard[] Cards, string[] Warnings, bool Loading = false, string? Layout = null,
    int? SourceCountHint = null);

/// <summary>One worker owns one page. DOM selectors are source-specific; typing and orchestration are shared.</summary>
public sealed class DomSourcePage : ISourcePage
{
    private readonly IPage page;
    private readonly Func<Task>? release;
    private readonly Action? onInterrupted;
    private readonly int operationTimeoutSeconds;
    private readonly AvitoMapCapture? mapCapture;
    private bool mapLayout;
    private MapDiagnosticCapture? mapDiagnostic;
    private bool diagnosticOnly;
    private string? requestedUrl;
    private MapSelectionSnapshot? lastMapSelection;
    public SourceSite Source { get; }
    public DomSourcePage(IPage page, SourceSite source, Func<Task>? release = null, int operationTimeoutSeconds = 30, int maximumMapBatches = 10,
        Action? onInterrupted = null)
    {
        this.page = page; this.release = release; this.operationTimeoutSeconds = operationTimeoutSeconds; this.onInterrupted = onInterrupted; Source = source;
        page.Response += OnResponse;
        // Retain the legacy public argument for callers; it no longer caps map response batches.
        _ = maximumMapBatches;
        if (source == SourceSite.Avito) mapCapture = new(page);
    }
    private void OnResponse(object? sender, IResponse response)
    { if (response.Request.IsNavigationRequest && response.Request.Frame == page.MainFrame) httpStatus = response.Status; }
    public string CurrentUrl => page.Url;
    private int httpStatus;
    private bool interrupted;
    private int disposed;
    public async Task OpenAsync(string url, CancellationToken cancellationToken)
    {
        _ = SearchUrls.SafePage(url, Source);
        requestedUrl = url;
        mapCapture?.Reset(url); mapLayout = false; lastMapSelection = null;
        IResponse? response;
        try { response = await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded }).WaitAsync(TimeSpan.FromSeconds(operationTimeoutSeconds), cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or PlaywrightException) { interrupted = true; throw; }
        httpStatus = response?.Status ?? 0;
    }
    private async Task<T> OperationAsync<T>(Func<Task<T>> operation)
    {
        try { return await operation().ConfigureAwait(false); }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or PlaywrightException or JsonException)
        { interrupted = true; throw; }
    }
    public Task<PageObservation> ReadAsync(CancellationToken cancellationToken) => diagnosticOnly
        ? throw new InvalidOperationException("Диагностика карты не собирает объявления.") : OperationAsync(() => ReadCoreAsync(cancellationToken));
    public Task BeginMapDiagnosticsAsync(string directory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Source != SourceSite.Avito || mapDiagnostic is not null) throw new InvalidOperationException("Выберите карту Avito.");
        diagnosticOnly = true; mapCapture?.Dispose();
        mapDiagnostic = new(page, directory); return Task.CompletedTask;
    }
    public async Task FinishMapDiagnosticsAsync(CancellationToken cancellationToken)
    {
        MapDiagnosticCapture capture = mapDiagnostic ?? throw new InvalidOperationException("Сначала откройте карту для диагностики.");
        List<string> errors = [];
        try
        {
            try
            {
                string selection = await page.EvaluateAsync<string>(MapSelectionScript, true)
                    .WaitAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
                await File.WriteAllTextAsync(Path.Combine(capture.DirectoryPath, "selection-state.json"), selection, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException or OperationCanceledException)
            { errors.Add("SELECTION_" + ex.GetType().Name); }
            try
            {
                string state = await page.EvaluateAsync<string>(MapDiagnosticCapture.DomStateScript).WaitAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
                await File.WriteAllTextAsync(Path.Combine(capture.DirectoryPath, "dom-state.json"), state, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException or OperationCanceledException) { errors.Add("DOM_" + ex.GetType().Name); }
            try
            {
                string? html = await CaptureErrorHtmlAsync(cancellationToken).ConfigureAwait(false);
                if (html is not null) await File.WriteAllTextAsync(Path.Combine(capture.DirectoryPath, "page.html"), html, cancellationToken).ConfigureAwait(false);
                else errors.Add("HTML_UNAVAILABLE");
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException or OperationCanceledException) { errors.Add("HTML_" + ex.GetType().Name); }
            try
            {
                await page.ScreenshotAsync(new() { Path = Path.Combine(capture.DirectoryPath, "screen.png"), FullPage = false, Timeout = 3000 })
                    .WaitAsync(TimeSpan.FromSeconds(4), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException or OperationCanceledException) { errors.Add("SCREEN_" + ex.GetType().Name); }
        }
        finally
        {
            await capture.DisposeAsync().ConfigureAwait(false);
            File.WriteAllText(Path.Combine(capture.DirectoryPath, "snapshot.json"), JsonSerializer.Serialize(new { Errors = errors, CapturedUtc = DateTimeOffset.UtcNow }));
            mapDiagnostic = null;
        }
    }
    public async Task<GeoPoint[][][]?> CaptureSearchAreaAsync(CancellationToken cancellationToken)
    {
        PageObservation snapshot = await ReadAsync(cancellationToken).ConfigureAwait(false);
        GeoPoint[][][]? area = (snapshot.Map ?? snapshot.AreaScope)?.ObservedAreas;
        return snapshot.Kind == PageKind.SearchResults && !snapshot.Loading && area is not null
            && SearchAreaGeometry.Signature(area) is not null ? area : null;
    }
    private async Task<PageObservation> ReadCoreAsync(CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(CurrentUrl, UriKind.Absolute, out Uri? uri) || !SearchUrls.IsPublic(uri, Source)) return new(PageKind.Unknown, [], ["UNEXPECTED_PAGE_URL"]);
        string json = await page.EvaluateAsync<string>("site => JSON.stringify((" + SnapshotScript + ")(site))", Source.ToString()).WaitAsync(TimeSpan.FromSeconds(operationTimeoutSeconds), cancellationToken).ConfigureAwait(false);
        DomSnapshot snapshot = JsonSerializer.Deserialize<DomSnapshot>(json) ?? throw new JsonException("DOM_SNAPSHOT_INVALID");
        if (httpStatus == 429) return new(PageKind.RateLimited, [], ["HTTP_429"]);
        if (httpStatus >= 400 && snapshot.Kind != "Captcha") return new(PageKind.SourceError, [], ["HTTP_" + httpStatus]);
        mapLayout = snapshot.Layout == "AVITO_MAP";
        if (Source == SourceSite.Avito && snapshot.Kind == "SearchResults" && !mapLayout
            && requestedUrl is not null && AvitoMapData.DrawId(requestedUrl) is not null)
        {
            // Read the saved list as-is. The source's map-link is only corroborating
            // evidence of its active draw reference; never click it or claim polygon proof.
            string stateJson = await page.EvaluateAsync<string>(AvitoListSearchStateScript)
                .WaitAsync(TimeSpan.FromSeconds(operationTimeoutSeconds), cancellationToken).ConfigureAwait(false);
            ListSearchState state = LocalJson.Read<ListSearchState>(stateJson);
            bool confirmed = AvitoSearchIdentity.ConfirmsListLinks(requestedUrl, CurrentUrl,
                state.Urls ?? (state.Url is null ? [] : [state.Url]), state.NeutralForm);
            PageObservation parsed = Parse(snapshot, Source, DateTimeOffset.UtcNow);
            PageObservation? captured = mapCapture?.Read(CurrentUrl, snapshot.Loading, parsed.Listings);
            HashSet<string> mainIds = parsed.Listings.Select(item => item.ExternalId).ToHashSet(StringComparer.Ordinal);
            ListingObservation[] raw = captured?.UnverifiedListings?.Where(item => mainIds.Contains(item.ExternalId)).ToArray() ?? parsed.Listings;
            return confirmed ? parsed with
                { AvitoListSearchConfirmed = true, AvitoNeutralFormConfirmed = state.NeutralForm, UnverifiedListings = raw, AreaScope = captured?.Map }
                : new(PageKind.SearchResults, [], [], UnverifiedListings: raw, AreaScope: captured?.Map, AvitoNeutralFormConfirmed: state.NeutralForm, SearchIdentityError:
                    state.Url is null ? "Список: страница не подтверждает исходную зону поиска"
                        : "Список: исходная зона или фильтры изменились либо условия формы не распознаны");
        }
        if (mapLayout && snapshot.Kind == "SearchResults" && mapCapture is not null)
        {
            PageObservation parsed = Parse(snapshot, Source, DateTimeOffset.UtcNow);
            PageObservation result = mapCapture.Read(CurrentUrl, snapshot.Loading, parsed.Listings);
            string selectionJson = await page.EvaluateAsync<string>(MapSelectionScript)
                .WaitAsync(TimeSpan.FromSeconds(operationTimeoutSeconds), cancellationToken).ConfigureAwait(false);
            MapSelectionState selection = JsonSerializer.Deserialize<MapSelectionState>(selectionJson) ?? new(false, false, false);
            using (JsonDocument selectionDocument = JsonDocument.Parse(selectionJson))
                lastMapSelection = new(DateTimeOffset.UtcNow, SearchUrls.DiagnosticUrl(CurrentUrl), selectionDocument.RootElement.Clone());
            // Cards and reset control can arrive before the map engine. Never treat that
            // intermediate shell as a settled selection or start scrolling its cards.
            result = result with { Map = result.Map! with { Selection = selection }, Loading = result.Loading || selection.Loading };
            // Geometry is measured only; this diagnostic read never scrolls or changes collection.
            string state = await page.EvaluateAsync<string>("() => {try {const s=(" + MapScrollScript + ")();return JSON.stringify({top:s.scrollTop,client:s.clientHeight,height:s.scrollHeight});} catch {return JSON.stringify({error:'MAP_SCROLL_CONTAINER_UNKNOWN'});}}")
                .WaitAsync(TimeSpan.FromSeconds(operationTimeoutSeconds), cancellationToken).ConfigureAwait(false);
            using JsonDocument geometry = JsonDocument.Parse(state);
            JsonElement root = geometry.RootElement;
            return result with { Diagnostic = result.Diagnostic! with {
                ScrollTop = root.TryGetProperty("top",out JsonElement top) ? top.GetDouble() : null,
                ClientHeight = root.TryGetProperty("client",out JsonElement client) ? client.GetDouble() : null,
                ScrollHeight = root.TryGetProperty("height",out JsonElement height) ? height.GetDouble() : null,
                ScrollError = root.TryGetProperty("error",out JsonElement error) ? error.GetString() : null },
                DataQuality = parsed.DataQuality };
        }
        return Parse(snapshot, Source, DateTimeOffset.UtcNow);
    }
    public const string MapSelectionScript = """
        (diagnostic=false) => {
          const identity = e => {
            const marker=e?.getAttribute('data-marker');
            return {Tag:e?.tagName?.toLowerCase()||null,Marker:/^map[-/a-z0-9]*$/.test(marker||'')?marker:null};
          };
          const rejection = e => {
            if (!e) return {Code:'ELEMENT_MISSING'};
            const r=e.getBoundingClientRect();
            if(r.width<=0||r.height<=0)return {Code:'ZERO_SIZE'};
            if(r.right<=0||r.bottom<=0||r.left>=innerWidth||r.top>=innerHeight)return {Code:'OUTSIDE_VIEWPORT'};
            let depth=0;
            for(let p=e;p;p=p.parentElement,depth++){
              const s=getComputedStyle(p);
              const code=s.display==='none'?'DISPLAY_NONE':s.visibility==='hidden'?'VISIBILITY_HIDDEN':Number(s.opacity)===0?'OPACITY_ZERO':null;
              if(code)return {Code:code,AncestorDepth:depth,Element:identity(p)};
            }
            return null;
          };
          const visible = e => rejection(e)===null;
          const candidates=[...document.querySelectorAll('[data-marker="map-view"],[data-marker="map-view-mfe"],[data-marker="map-full"]')];
          const maps=candidates.filter(visible);
          const large = e => e.getBoundingClientRect().width>=200&&e.getBoundingClientRect().height>=150;
          // Yandex Maps 3 renders raster tiles without a large canvas or SVG.
          // Only loaded, visible tiles inside an Avito map can confirm its surface.
          const surface=maps.some(m=>
            [...m.querySelectorAll('canvas,svg')].some(e=>visible(e)&&large(e)) ||
            (large(m)&&[...m.querySelectorAll('.ymaps3--tile-layer img')].some(e=>e.complete&&e.naturalWidth>0&&visible(e))));
          const isReset = e => /сбросить.*област/i.test((e.textContent||'')+' '+(e.getAttribute('title')||'')+' '+(e.getAttribute('aria-label')||''));
          const reset=maps.some(m=>[...m.querySelectorAll('button,[role="button"]')].some(e=>visible(e)&&!e.disabled&&isReset(e)));
          const loading=maps.some(m=>[...m.querySelectorAll('[class*="mapLoader-"],[class*="loading-"],[data-marker*="loader"],[aria-busy="true"]')].some(visible));
          // The map can be ready while unrelated page resources still load.
          // Surface, active selection, loading and geometry remain separate gates.
          const documentReady=document.readyState==='interactive'||document.readyState==='complete';
          const state={MapVisible:documentReady&&maps.length>0,SurfaceVisible:surface,ResetVisible:reset,Loading:loading};
          if(diagnostic){
            const describe = e => {
              const r=e.getBoundingClientRect();
              return {...identity(e),Width:r.width,Height:r.height,Left:r.left,Top:r.top,Rejection:rejection(e)};
            };
            state.Diagnostic={
              ReadyState:document.readyState,Viewport:{Width:innerWidth,Height:innerHeight},
              MapCandidates:candidates.length,Truncated:candidates.length>8,
              Maps:candidates.slice(0,8).map(m=>{
                const surfaces=[...m.querySelectorAll('canvas,svg')];
                const tiles=[...m.querySelectorAll('.ymaps3--tile-layer img')];
                const buttons=[...m.querySelectorAll('button,[role="button"]')].filter(isReset);
                const loaders=[...m.querySelectorAll('[class*="mapLoader-"],[class*="loading-"],[data-marker*="loader"],[aria-busy="true"]')];
                return {
                  Map:describe(m),Large:large(m),
                  SurfaceCount:surfaces.length,LargeSurfaces:surfaces.filter(large).slice(0,6).map(describe),
                  TileCount:tiles.length,Tiles:tiles.slice(0,6).map(e=>({...describe(e),Complete:!!e.complete,NaturalWidth:e.naturalWidth})),
                  ResetCount:buttons.length,ResetButtons:buttons.slice(0,4).map(e=>({...describe(e),Disabled:!!e.disabled})),
                  LoaderCount:loaders.length,Loaders:loaders.slice(0,4).map(describe)
                };
              })
            };
          }
          return JSON.stringify(state);
        }
        """;
    public static PageObservation Parse(DomSnapshot snapshot, SourceSite source, DateTimeOffset now)
    {
        if (!Enum.TryParse(snapshot.Kind, out PageKind kind)) kind = PageKind.Unknown;
        if (kind != PageKind.SearchResults) return new(kind, [], snapshot.Warnings);
        List<ListingObservation> items = []; List<ListingDataQualityDiagnostic> quality = [];
        List<string> errors = new(snapshot.Warnings); HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (DomCard card in snapshot.Cards)
        {
            if (!Regex.IsMatch(card.Id, @"^\d{5,20}$") || !Uri.TryCreate(card.Url, UriKind.Absolute, out Uri? url) || !SearchUrls.IsPublic(url, source)
                || !(source == SourceSite.Avito ? Regex.IsMatch(url.AbsolutePath, "_" + card.Id + "/?$") : Regex.IsMatch(url.AbsolutePath, "/sale/(?:suburban|flat)/" + card.Id + "/?$")))
            { errors.Add("INVALID_ID_OR_URL"); continue; }
            if (!ids.Add(card.Id)) continue;
            List<string> warnings = [];
            List<AreaAssertion> structuredAreas = [], titleAreas = [], descriptionAreas = [];
            string? description = card.StructuredDescription ?? card.Description;
            string? location = card.StructuredLocation ?? card.Location;
            AddStructuredArea(structuredAreas, card.StructuredArea, card.StructuredAreaUnit, warnings);
            AddAreas(titleAreas, card.Title, "Title");
            // Description contributes only an explicit plot-area statement, never an arbitrary number near a road or house.
            foreach (Match match in Regex.Matches(description ?? "", @"(?:площадь(?:\s+участка)?\s*[:—-]\s*|участок\s+)(\d+(?:[.,]\d+)?\s*(?:сот(?:ок|ки|ка|\.)?|га\b|м[²2]))", RegexOptions.IgnoreCase))
                AddAreas(descriptionAreas, match.Groups[1].Value, "Description");
            AreaAssertion[] assertions = [.. structuredAreas, .. titleAreas, .. descriptionAreas];
            AreaAssertion? selectedArea = SelectArea(titleAreas)
                ?? SelectArea(structuredAreas)
                ?? SelectArea(descriptionAreas);
            AreaAssertion[] comparableAreas = [.. PlotAreaCandidates(titleAreas), .. structuredAreas, .. PlotAreaCandidates(descriptionAreas)];
            if ((selectedArea != null && comparableAreas.Any(area => Math.Abs(area.SquareMeters - selectedArea.SquareMeters) > 1))
                || (selectedArea == null && comparableAreas.Length > 1
                    && comparableAreas.Any(area => Math.Abs(area.SquareMeters - comparableAreas[0].SquareMeters) > 1)))
                warnings.Add("AREA_CONFLICT");
            NumberValue price = NumberValue.Read(card.Price ?? card.StructuredPrice), unit = NumberValue.Read(card.UnitPrice);
            if (price.Presence != Presence.Present) warnings.Add("PRICE_" + price.Presence);
            if (assertions.Length == 0) warnings.Add("AREA_ABSENT");
            string? assignment = Regex.Match(card.Title ?? "", @"\b(?:ИЖС|СНТ|ДНП|ЛПХ)\b", RegexOptions.IgnoreCase) is { Success: true } use ? use.Value : null;
            LandType[] declaredLandTypes = CianDeclaredLandTypes(source, card.DeclaredLandTypes, warnings);
            LandType[] inferredLandTypes = LandTypeClassifier.Infer(card.Title, description);
            string? statistics = card.SellerStatistics;
            Match completed = Regex.Match(statistics ?? "", @"(\d+)\s+заверш[её]н", RegexOptions.IgnoreCase);
            TextValue sellerType = TextValue.Read(card.SellerType ?? card.Badges.FirstOrDefault(x => Regex.IsMatch(x, "^(Собственник|Агент|Агентство|Застройщик|Риелтор)$", RegexOptions.IgnoreCase)));
            string[] photos = (card.StructuredPhotos ?? []).Concat(card.Photos)
                .Where(x => Uri.TryCreate(x, UriKind.Absolute, out Uri? image) && image.Scheme == "https" && image.UserInfo.Length == 0
                    && (image.Host.EndsWith(".avito.st", StringComparison.OrdinalIgnoreCase) || image.Host == "avito.st"
                    || image.Host.EndsWith(".cdn-cian.ru", StringComparison.OrdinalIgnoreCase) || image.Host == "cdn-cian.ru"))
                .Select(x => new Uri(x).GetLeftPart(UriPartial.Path)).Distinct(StringComparer.Ordinal).ToArray();
            string? sellerUrl = null;
            if (Uri.TryCreate(card.SellerUrl, UriKind.Absolute, out Uri? seller) && SearchUrls.IsPublic(seller, source)
                && Regex.IsMatch(seller.AbsolutePath, @"^/(?:brands|user|agents|company|developers)/[a-zA-Z0-9_-]+/?$")) sellerUrl = seller.GetLeftPart(UriPartial.Path);
            if (description?.Length > 100000) { description = description[..100000]; warnings.Add("DESCRIPTION_TRUNCATED_100000"); }
            NumberValue latitude = Coordinate(card.Latitude, -90, 90, warnings);
            NumberValue longitude = Coordinate(card.Longitude, -180, 180, warnings);
            DateTimeOffset? sourcePublished = SourcePublished(card.SourcePublishedUnix, now, warnings);
            bool structured = source == SourceSite.Cian && (card.StructuredArea != null || card.StructuredDescription != null
                || card.StructuredLocation != null || card.SourcePublishedUnix != null || card.Latitude != null
                || card.Longitude != null || card.CadastralNumber != null || (card.DeclaredLandTypes?.Length ?? 0) > 0
                || (card.StructuredPhotos?.Length ?? 0) > 0);
            ListingObservation observation = new()
            {
                Source = source,
                ExternalId = card.Id,
                Url = url.GetLeftPart(UriPartial.Path),
                ObservedAtUtc = now,
                Title = TextValue.Read(card.Title),
                Price = price,
                UnitPrice = unit,
                AreaSquareMeters = new(assertions.Length == 0 ? Presence.Absent : selectedArea != null ? Presence.Present : Presence.ParseFailed,
                    assertions.Length == 0 ? null : selectedArea?.Raw ?? string.Join("; ", assertions.Select(x => x.Raw)),
                    selectedArea?.SquareMeters),
                Areas = assertions,
                DerivedPricePerSotka = selectedArea?.SquareMeters > 0 && price.Parsed.HasValue
                    ? price.Parsed / (selectedArea.SquareMeters / 100) : null,
                Assignment = TextValue.Read(assignment),
                Location = TextValue.Read(location),
                Transport = TextValue.Read(card.Transport),
                Description = TextValue.Read(description),
                DateText = TextValue.Read(card.Date),
                CadastralNumber = TextValue.Read(card.CadastralNumber),
                SourcePublishedAtUtc = sourcePublished,
                DeclaredLandTypes = declaredLandTypes,
                InferredLandTypes = inferredLandTypes,
                Latitude = latitude,
                Longitude = longitude,
                SellerName = TextValue.Read(card.Seller),
                SellerUrl = TextValue.Read(sellerUrl),
                SellerStatistics = TextValue.Read(statistics),
                SellerType = sellerType,
                CompletedAdvertisements = completed.Success ? new(Presence.Present, statistics, decimal.Parse(completed.Groups[1].Value, CultureInfo.InvariantCulture)) : NumberValue.Read(null),
                Badges = card.Badges.Distinct(StringComparer.Ordinal).ToArray(),
                PhotoUrls = photos,
                Warnings = warnings.ToArray(),
                Provenance = structured ? "DOM+Structured" : "DOM",
                AdapterVersion = structured ? "1.1" : "1.0"
            };
            items.Add(observation);
            List<string> missing = [];
            if (observation.Price.Parsed is null) missing.Add("Price");
            if (observation.AreaSquareMeters.Parsed is null) missing.Add("Area");
            if (observation.Location.Presence != Presence.Present || string.IsNullOrWhiteSpace(observation.Location.Raw)) missing.Add("Location");
            if (missing.Count > 0)
                quality.Add(new(observation.ExternalId, observation.Title.Raw, missing.ToArray(),
                    observation.Price.Presence, observation.AreaSquareMeters.Presence, observation.Location.Presence,
                    observation.Price.Raw, observation.AreaSquareMeters.Raw, observation.Location.Raw,
                    (card.LocationCandidates ?? []).Where(value => !string.IsNullOrWhiteSpace(value)).Take(12).ToArray(),
                    card.StructuredLocation, card.StructuredLocationHidden,
                    observation.Latitude.Parsed, observation.Longitude.Parsed,
                    observation.Provenance, observation.AdapterVersion, observation.Warnings));
        }
        return new(kind, items.ToArray(), errors.ToArray(), snapshot.Loading, snapshot.Layout,
            SourceCountHint: snapshot.SourceCountHint, DataQuality: quality.ToArray());
    }
    private static void AddStructuredArea(List<AreaAssertion> result, string? raw, string? unit, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(raw)) return;
        if (!decimal.TryParse(raw.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value) || value <= 0)
        { warnings.Add("STRUCTURED_AREA_INVALID"); return; }
        decimal multiplier = unit switch
        {
            "sotka" => 100m,
            "hectare" or "hectar" => 10000m,
            "squareMeter" or "squareMeters" => 1m,
            _ => 0m
        };
        if (multiplier == 0) { warnings.Add("STRUCTURED_AREA_UNIT_UNKNOWN"); return; }
        result.Add(new("Structured", $"{raw} {unit}", value * multiplier));
    }
    private static NumberValue Coordinate(decimal? value, decimal minimum, decimal maximum, List<string> warnings)
    {
        if (value == null) return NumberValue.Read(null);
        string raw = value.Value.ToString(CultureInfo.InvariantCulture);
        if (value < minimum || value > maximum) { warnings.Add("COORDINATES_INVALID"); return new(Presence.ParseFailed, raw, null); }
        return new(Presence.Present, raw, value);
    }
    private static DateTimeOffset? SourcePublished(long? unix, DateTimeOffset now, List<string> warnings)
    {
        if (unix == null) return null;
        try
        {
            DateTimeOffset value = DateTimeOffset.FromUnixTimeSeconds(unix.Value);
            if (value < DateTimeOffset.UnixEpoch || value > now.AddDays(1)) { warnings.Add("SOURCE_PUBLISHED_AT_INVALID"); return null; }
            return value;
        }
        catch (ArgumentOutOfRangeException) { warnings.Add("SOURCE_PUBLISHED_AT_INVALID"); return null; }
    }
    private static LandType[] CianDeclaredLandTypes(SourceSite source, string[]? values, List<string> warnings)
    {
        if (source != SourceSite.Cian || values == null || values.Length == 0) return [];
        List<LandType> result = [];
        foreach (string raw in values.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            LandType? value = raw.Trim().ToLowerInvariant() switch
            {
                "individualhousingconstruction" or "ижс" => LandType.Izhs,
                "privatefarm" or "лпх" => LandType.Lph,
                "gardening" or "садоводство" => LandType.Gardening,
                "snt" or "снт" => LandType.Snt,
                "dnp" or "днп" => LandType.Dnp,
                "farm" or "kfh" or "кфх" => LandType.Kfh,
                "industrial" => LandType.Industrial,
                "other" => LandType.Other,
                _ => null
            };
            if (value.HasValue) result.Add(value.Value); else warnings.Add("LAND_TYPE_UNMAPPED");
        }
        return result.Distinct().ToArray();
    }
    private static AreaAssertion? SelectArea(IReadOnlyList<AreaAssertion> areas)
    {
        AreaAssertion[] candidates = PlotAreaCandidates(areas);
        if (candidates.Length == 0) return null;
        AreaAssertion first = candidates[0];
        return candidates.Any(area => Math.Abs(area.SquareMeters - first.SquareMeters) > 1) ? null : first;
    }

    private static AreaAssertion[] PlotAreaCandidates(IReadOnlyList<AreaAssertion> areas)
    {
        AreaAssertion[] landUnits = areas.Where(area => Regex.IsMatch(area.Raw, @"(?:сот|\bга\b)", RegexOptions.IgnoreCase)).ToArray();
        return landUnits.Length > 0 ? landUnits : areas.ToArray();
    }

    private static void AddAreas(List<AreaAssertion> result, string? text, string origin)
    {
        foreach (Match match in Regex.Matches(text ?? "", @"(\d+(?:[.,]\d+)?)\s*(сот(?:ок|ки|ка|\.)?|га\b|м[²2])", RegexOptions.IgnoreCase))
        {
            decimal value = decimal.Parse(match.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
            decimal multiplier = match.Groups[2].Value.StartsWith("сот", StringComparison.OrdinalIgnoreCase) ? 100 : match.Groups[2].Value.StartsWith("га", StringComparison.OrdinalIgnoreCase) ? 10000 : 1;
            result.Add(new(origin, match.Value, value * multiplier));
        }
    }
    public Task<bool> WheelAsync(CollectionSettings settings, CancellationToken cancellationToken) => OperationAsync(() => WheelCoreAsync(settings, cancellationToken));
    private async Task<bool> WheelCoreAsync(CollectionSettings settings, CancellationToken cancellationToken)
    {
        // Small wheel events load the DOM gradually. This is rendering control, not fingerprint masking.
        float[] point = await page.EvaluateAsync<float[]>(mapLayout ? "() => { const s = (" + MapScrollScript + ")(); const r=s.getBoundingClientRect(); return [Math.max(1,Math.min(innerWidth-1,r.left+r.width/2)),Math.max(1,Math.min(innerHeight-1,r.top+r.height/2))]; }" : "() => [innerWidth/2, innerHeight*0.6]").WaitAsync(TimeSpan.FromSeconds(operationTimeoutSeconds), cancellationToken).ConfigureAwait(false);
        await page.Mouse.MoveAsync(point[0], point[1]).WaitAsync(TimeSpan.FromSeconds(operationTimeoutSeconds), cancellationToken).ConfigureAwait(false);
        for (int i = 0; i < settings.ScrollBatchEvents; i++)
        {
            await page.Mouse.WheelAsync(0, settings.WheelDelta).WaitAsync(TimeSpan.FromSeconds(operationTimeoutSeconds), cancellationToken).ConfigureAwait(false);
            await Task.Delay(settings.WheelIntervalMilliseconds, cancellationToken).ConfigureAwait(false);
        }
        return await page.EvaluateAsync<bool>(mapLayout ? "() => {const s=(" + MapScrollScript + ")(); return s.scrollTop+s.clientHeight>=s.scrollHeight-4;}" : "() => window.scrollY + innerHeight >= document.documentElement.scrollHeight - 4").WaitAsync(TimeSpan.FromSeconds(operationTimeoutSeconds), cancellationToken).ConfigureAwait(false);
    }
    public Task<Pagination> NextAsync(CancellationToken cancellationToken) => OperationAsync(() => NextCoreAsync(cancellationToken));
    private async Task<Pagination> NextCoreAsync(CancellationToken cancellationToken)
    {
        if (mapLayout) return new(NextKind.End, Reason: "Конец правого списка карты");
        string json = await page.EvaluateAsync<string>("site => JSON.stringify((" + PaginationScript + ")(site))", Source.ToString()).WaitAsync(TimeSpan.FromSeconds(operationTimeoutSeconds), cancellationToken).ConfigureAwait(false);
        Pagination pagination = LocalJson.Read<Pagination>(json);
        if (pagination.Kind != NextKind.Next || pagination.Url is null) return pagination;
        try
        {
            string safe = SearchUrls.SafePage(pagination.Url, Source);
            if (!SearchUrls.SameSearch(CurrentUrl, safe, Source)) return new(NextKind.UnknownInvalid, pagination.Url, pagination.Selector, "Пагинация меняет фильтры поиска");
            if (SearchUrls.PageNumber(safe) != SearchUrls.PageNumber(CurrentUrl) + 1) return new(NextKind.UnknownInvalid, Reason: "Неверный номер следующей страницы");
            return pagination;
        }
        catch (ArgumentException) { return new(NextKind.UnknownInvalid, Reason: "Небезопасная ссылка пагинации"); }
    }
    public async Task FollowAsync(Pagination pagination, CancellationToken cancellationToken)
    {
        if (pagination.Kind != NextKind.Next || pagination.Url is null || pagination.Selector is null) throw new ArgumentException("NEXT_REQUIRED");
        Pagination current = await NextAsync(cancellationToken).ConfigureAwait(false);
        if (current != pagination) throw new InvalidOperationException("PAGINATION_CHANGED");
        ILocator candidates = page.Locator(pagination.Selector).Filter(new() { Visible = true });
        for (int i = 0; i < await candidates.CountAsync().WaitAsync(TimeSpan.FromSeconds(operationTimeoutSeconds), cancellationToken).ConfigureAwait(false); i++)
        {
            ILocator candidate = candidates.Nth(i);
            if (await candidate.EvaluateAsync<string>("e => e.href").WaitAsync(TimeSpan.FromSeconds(operationTimeoutSeconds), cancellationToken).ConfigureAwait(false) != pagination.Url) continue;
            string previous = await page.EvaluateAsync<string>("site => JSON.stringify((" + SnapshotScript + ")(site).Cards.map(c => c.Id))", Source.ToString()).WaitAsync(TimeSpan.FromSeconds(operationTimeoutSeconds), cancellationToken).ConfigureAwait(false);
            await page.EvaluateAsync("() => window.__landerPreviousDocument = document.documentElement").WaitAsync(TimeSpan.FromSeconds(operationTimeoutSeconds), cancellationToken).ConfigureAwait(false);
            try
            {
                // SPA pagination may change the URL and cards without any navigation document response.
                await candidate.ClickAsync().WaitAsync(TimeSpan.FromSeconds(operationTimeoutSeconds), cancellationToken).ConfigureAwait(false);
                await page.WaitForFunctionAsync("expected => Number(new URL(location.href).searchParams.get('p') || 1) === expected",
                    SearchUrls.PageNumber(pagination.Url), new() { Timeout = operationTimeoutSeconds * 1000 })
                    .WaitAsync(TimeSpan.FromSeconds(operationTimeoutSeconds), cancellationToken).ConfigureAwait(false);
                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded).WaitAsync(TimeSpan.FromSeconds(operationTimeoutSeconds), cancellationToken).ConfigureAwait(false);
                // A changed address alone is insufficient: don't attribute stale cards to the next page.
                await page.WaitForFunctionAsync("arg => { const s = (" + SnapshotScript + ")(arg.site); return window.__landerPreviousDocument !== document.documentElement || (!s.Loading && (s.Kind !== 'SearchResults' || JSON.stringify(s.Cards.map(c => c.Id)) !== arg.previous)); }",
                    new { site = Source.ToString(), previous }, new() { Timeout = operationTimeoutSeconds * 1000 })
                    .WaitAsync(TimeSpan.FromSeconds(operationTimeoutSeconds), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { interrupted = true; throw; }
            catch (TimeoutException) { interrupted = true; throw; }
            catch (PlaywrightException) { interrupted = true; throw; }
            if (SearchUrls.PageNumber(CurrentUrl) != SearchUrls.PageNumber(pagination.Url)) throw new InvalidOperationException("PAGE_NAVIGATION_MISMATCH");
            if (!SearchUrls.SameSearch(pagination.Url, CurrentUrl, Source)) throw new InvalidOperationException("PAGE_FILTER_MISMATCH");
            return;
        }
        throw new InvalidOperationException("NEXT_LINK_DISAPPEARED");
    }
    public async Task<SourcePageDiagnostic?> CaptureDiagnosticAsync(string? candidateUrl, CancellationToken cancellationToken)
    {
        string paginationJson = await page.EvaluateAsync<string>("site => JSON.stringify((" + PaginationScript + ")(site))", Source.ToString())
            .WaitAsync(TimeSpan.FromSeconds(operationTimeoutSeconds), cancellationToken).ConfigureAwait(false);
        Pagination pagination = LocalJson.Read<Pagination>(paginationJson);
        string elementsJson = await page.EvaluateAsync<string>("""
            () => {
                const visible=e=>!!e&&!!e.getClientRects().length;
                const roots=[...document.querySelectorAll('[data-name*="Pagination"],[data-marker*="pagination"],[class*="pagination" i]')].filter(visible).slice(0,8);
                const nodes=[...new Set(roots.flatMap(root=>[root,...root.querySelectorAll('a,button,span')]))].filter(visible).slice(0,30);
                return JSON.stringify(nodes.map(e=>({tag:e.tagName.toLowerCase(),text:(e.innerText||e.textContent||'').trim().slice(0,120),
                  href:e.href||null,dataName:e.getAttribute('data-name'),dataMarker:e.getAttribute('data-marker'),className:String(e.className||'').slice(0,200)})));
            }
            """).WaitAsync(TimeSpan.FromSeconds(operationTimeoutSeconds), cancellationToken).ConfigureAwait(false);
        PaginationElementDiagnostic[] elements = LocalJson.Read<PaginationElementDiagnostic[]>(elementsJson);
        string? candidate = candidateUrl ?? pagination.Url;
        return new(CurrentUrl, candidate, pagination.Kind.ToString(), pagination.Reason ?? "",
            elements, SearchUrls.DiagnosticIdentityDiff(CurrentUrl, candidate, Source));
    }

    public async Task<MapSelectionErrorEvidence?> CaptureMapSelectionErrorAsync(CancellationToken cancellationToken)
    {
        if (Source != SourceSite.Avito) return null;
        // Preserve the collection decision even if the DOM changes or closes
        // before this second, diagnostic-only read. Never resumes or mutates the page.
        MapSelectionSnapshot? lastRead = lastMapSelection;
        try
        {
            string state = await page.EvaluateAsync<string>(MapSelectionScript, true)
                .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            using JsonDocument document = JsonDocument.Parse(state);
            return new(lastRead, new(DateTimeOffset.UtcNow, SearchUrls.DiagnosticUrl(CurrentUrl), document.RootElement.Clone()), null);
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException or OperationCanceledException or JsonException or InvalidOperationException)
        { return new(lastRead, null, ex.GetType().Name); }
    }

    public async Task<string?> CaptureErrorHtmlAsync(CancellationToken cancellationToken)
    {
        if (page.IsClosed || !Uri.TryCreate(CurrentUrl, UriKind.Absolute, out Uri? uri) || !SearchUrls.IsPublic(uri, Source)) return null;
        return await page.EvaluateAsync<string?>(ErrorHtmlScript, LastErrorSnapshot.MaximumHtmlCharacters)
            .WaitAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
    }

    // Work on a clone: capturing a diagnostic must never alter the live search or expose inline application state.
    public const string ErrorHtmlScript = """
        maximum => {
            const root = document.documentElement.cloneNode(true);
            root.querySelectorAll('script,noscript,style,iframe,object,embed,template,base,link,meta').forEach(e => e.remove());
            root.querySelectorAll('input,textarea,select,[contenteditable]').forEach(e => {
                e.removeAttribute('value'); e.removeAttribute('checked'); e.removeAttribute('selected');
                if (e.tagName !== 'INPUT') e.textContent = '';
            });
            const sensitive = /token|password|passwd|secret|authorization|cookie|session|csrf|jwt|api.?key|auth.?key|signature/i;
            const cleanUrl = value => {
                if (!value || value.startsWith('#')) return value;
                try {
                    const url = new URL(value, document.baseURI);
                    if (!['https:', 'http:'].includes(url.protocol) || url.username || url.password) return '';
                    url.hash = '';
                    for (const key of [...url.searchParams.keys()])
                        if (sensitive.test(key) || /^(auth|sid|key)$/i.test(key)) url.searchParams.delete(key);
                    return url.href;
                } catch { return ''; }
            };
            for (const e of [root, ...root.querySelectorAll('*')]) {
                for (const attribute of [...e.attributes]) {
                    const name = attribute.name;
                    if (/^on/i.test(name) || sensitive.test(name) || /^(value|nonce|srcdoc|srcset|style|action|formaction|ping)$/i.test(name)
                        || /^data-/i.test(name) && !/^data-(marker|name|testid)$/i.test(name)) e.removeAttribute(name);
                    else if (/^(href|src|poster|cite|background|xlink:href)$/i.test(name)) e.setAttribute(name, cleanUrl(attribute.value));
                }
                for (const node of [...e.childNodes]) if (node.nodeType === 8) node.remove();
            }
            const head = root.querySelector('head');
            if (head) {
                const policy = document.createElement('meta');
                policy.setAttribute('http-equiv', 'Content-Security-Policy');
                policy.setAttribute('content', "default-src 'none'; style-src 'unsafe-inline'; img-src data:; form-action 'none'; base-uri 'none'");
                head.prepend(policy);
            }
            const html = '<!DOCTYPE html>\n' + root.outerHTML;
            return html.length <= maximum ? html : null;
        }
        """;

    public async Task<bool> CaptureScreenshotAsync(string path, CancellationToken cancellationToken)
    {
        await page.ScreenshotAsync(new() { Path = path, FullPage = false })
            .WaitAsync(TimeSpan.FromSeconds(operationTimeoutSeconds), cancellationToken).ConfigureAwait(false);
        return File.Exists(path);
    }

    public Task ActivateAsync(CancellationToken cancellationToken) => OperationAsync(async () =>
    { await page.BringToFrontAsync().WaitAsync(TimeSpan.FromSeconds(operationTimeoutSeconds), cancellationToken).ConfigureAwait(false); return true; });
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        page.Response -= OnResponse;
        if (mapDiagnostic is not null) await FinishMapDiagnosticsAsync(CancellationToken.None).ConfigureAwait(false);
        mapCapture?.Dispose();
        try
        {
            if (release is null || interrupted) await BrowserLifetime.CloseAsync(page.CloseAsync()).ConfigureAwait(false);
        }
        catch
        {
            // Successful Close terminates pending commands on this page. If Close failed,
            // quarantine the context rather than recycle an unsafe page into another job.
            if (interrupted) onInterrupted?.Invoke();
            throw;
        }
        finally
        {
            if (release is not null) await release().ConfigureAwait(false);
        }
    }

    // Find the list's own overflow container, never the map or the document (wheel would zoom the map).
    public const string MapScrollScript = """
        () => {
          const side=document.querySelector('[data-marker="map-full/side-block"]');
          const list=side?.querySelector('[data-marker="listItems"]');
          if(!side || !list) throw Error('MAP_LIST_NOT_FOUND');
          for(let e=list;e && e!==document.body;e=e.parentElement) {
            const style=getComputedStyle(e);
            if(/auto|scroll/.test(style.overflowY) && e.clientHeight>0 && e.scrollHeight>e.clientHeight+4) return e;
            if(e===side) break;
          }
          // A short single screen is a valid bottom; a tall unscrollable layout is unknown.
          if(list.getBoundingClientRect().bottom <= Math.min(innerHeight,side.getBoundingClientRect().bottom)+4) return side;
          throw Error('MAP_SCROLL_CONTAINER_UNKNOWN');
        }
        """;
    private sealed record ListSearchState(string? Url, bool NeutralForm, string[]? Urls = null);

    public const string AvitoListSearchStateScript = """
        () => {
          const visible=e=>!!e&&!!e.getClientRects().length;
          const links=[...document.querySelectorAll('a[data-marker="map-button"],a[data-marker="minimap-banner"]')].filter(visible);
          const urls=[...new Set(links.map(a=>a.href))];
          const root=[...document.querySelectorAll('[data-marker="search-filters"]')].find(visible);
          // Avito's native selects may be empty: their displayed selection lives
          // in the adjacent /select-text span. If both carry text they must agree.
          const text=marker=>{
            const controls=root?.querySelectorAll('[data-marker="'+marker+'"]');
            const labels=root?.querySelectorAll('[data-marker="'+marker+'-text"]');
            if(controls?.length!==1 || labels.length>1) return null;
            const control=controls[0], label=labels[0];
            const clean=e=>(e?.innerText||e?.textContent||'').replace(/\s+/g,' ').trim();
            if(label && (label.parentElement!==control.parentElement || !visible(label))) return null;
            if(control.tagName==='SELECT' && (control.multiple || control.selectedOptions.length>1)) return null;
            const native=control.tagName==='SELECT' ? clean(control.selectedOptions[0]) : clean(control);
            const displayed=clean(label);
            return displayed && native && displayed!==native ? null : displayed||native;
          };
          const allowedInputs=new Set(['price-from/input','price-to/input','params[149569]/input']);
          // Geography is verified independently by the unchanged URL and source draw
          // reference. Its ordinary selector is not an unknown membership control.
          const allowedSelects=new Set(['categoryId/select','params[203]/select','geoFilters/select']);
          const neutral=!!root && text('categoryId/select')==='Земельные участки'
            && text('params[203]/select')==='Купить или снять'
            && [...root.querySelectorAll('[data-marker$="/select"]')].every(e=>allowedSelects.has(e.getAttribute('data-marker')))
            && [...root.querySelectorAll('input,textarea,select')].every(e=>{
              if(e.tagName==='SELECT') return allowedSelects.has(e.getAttribute('data-marker'));
              if(e.type==='radio') return !e.checked || e.closest('label')?.innerText.trim()==='Все';
              if(e.type==='checkbox') return e.name==='withImagesOnly' && !e.checked;
              if(e.type==='hidden') return e.name==='params[156912]';
              return allowedInputs.has(e.getAttribute('data-marker')) && !e.value.trim();
            });
          return JSON.stringify({url:urls[0]||null,urls,neutralForm:neutral});
        }
        """;

    public const string SnapshotScript = """
        site => {
          const avito = site === 'Avito';
          const cianConfig = !avito && window._cianConfig && Array.isArray(window._cianConfig['frontend-serp'])
            ? window._cianConfig['frontend-serp'] : [];
          const cianInitial = cianConfig.find(x => x && x.key === 'initialState')?.value;
          const cianResults = cianInitial && cianInitial.results;
          const cianOffers = Array.isArray(cianResults?.offers) ? cianResults.offers : [];
          const cianById = new Map(cianOffers.map(x => [String(x?.cianId ?? x?.id ?? ''), x]).filter(x => x[0]));
          const sourceCountHint = Number.isInteger(cianResults?.totalOffers) && cianResults.totalOffers >= 0 ? cianResults.totalOffers : null;
          const visible = e => !!e && !!e.getClientRects().length;
          const read = (root, selector, full=false) => {
            const e = [...root.querySelectorAll(selector)].find(visible);
            return e ? (full ? e.textContent : e.innerText).replace(/\r/g,'').trim() : null;
          };
          const heading = read(document,'h1') || '';
          const message = heading+' '+document.title+' '+(read(document,'[data-marker="error-page"], [data-name="ErrorPage"], #captcha')||'');
          const has = selector => [...document.querySelectorAll(selector)].some(visible);
          let kind = /слишком много запросов|too many requests|429/i.test(message) ? 'RateLimited'
            : has('iframe[src*="captcha"], [data-marker="captcha"], #captcha, [data-name="Captcha"]') || /капч|captcha|не робот|доступ ограничен|проверка.*браузер/i.test(message) ? 'Captcha'
            : has('input[type="password"]') || /^(вход|авторизация)/i.test(heading) ? 'AuthenticationRequired'
            : /сервис недоступен|ошибка сервера|страница не найдена|service unavailable/i.test(message) ? 'SourceError' : 'Unknown';
          const map = avito && has('[data-marker="map-full/side-block"]');
          const main = document.querySelector(map ? '[data-marker="map-full/side-block"]' : avito ? '[data-marker="catalog-serp"]' : '[data-name="Offers"]');
          if (kind === 'Unknown' && main) kind = 'SearchResults';
          if (kind !== 'SearchResults') return {Kind:kind,Cards:[],Warnings:[]};
          const cut = [...main.querySelectorAll('h2,h3,[data-marker*="recommend"],[data-name*="Recommendation"]')]
            .filter(e => /других регион|других город|рекоменд|похожие/i.test(e.textContent) || /recommend/i.test(e.getAttribute('data-marker')||e.getAttribute('data-name')||''));
          const roots = (map ? [...main.querySelectorAll('a[data-marker="title"]')].map(a=>a.closest('[data-marker=""]')).filter(Boolean) : [...main.querySelectorAll(avito?'[data-marker="item"]':'[data-name="CardComponent"]')])
            .filter(e => visible(e) && !e.closest('[data-marker*="advert"],[data-marker*="recommend"],[data-name*="Recommendation"],[data-name="Advertising"]')
              && !cut.some(s=>!!(s.compareDocumentPosition(e)&Node.DOCUMENT_POSITION_FOLLOWING)));
          const cards = roots.map(e => {
            const a = e.querySelector(avito?'a[data-marker="item-title"],a[data-marker="title"]':'a[href*="/sale/"]');
            const id = avito ? e.getAttribute('data-item-id') || a?.href.match(/_(\d+)(?:\?|$)/)?.[1] : a?.href.match(/\/sale\/[^/]+\/(\d+)\//)?.[1];
            const structured = !avito && id ? cianById.get(String(id)) : null;
            const land = structured?.land;
            const coordinates = structured?.geo?.coordinates;
            const structuredPhotos = Array.isArray(structured?.photos) ? structured.photos.map(x=>x?.fullUrl).filter(Boolean) : [];
            const rawLandStatus = land?.status;
            const declaredLandTypes = Array.isArray(rawLandStatus) ? rawLandStatus.map(String) : rawLandStatus ? [String(rawLandStatus)] : [];
            const seller = e.querySelector(avito?'[class*="userInfoStep"] a[href], a[data-marker="seller-link/link"], a[href*="/brands/"], a[href*="/user/"]':'a[href*="/agents/"],a[href*="/company/"],a[href*="/developers/"]');
            const description = read(e,avito?'[data-marker="item-description"], [class*="bottomBlock"] > [class*="ivaItemRedesign"] > p[style*="module-max-lines"], [class*="bottomBlock"] > div:first-child > p':'[data-name="Description"]',true);
            const price = avito ? read(e,'[data-marker="item-price"]') : read(e,'[data-mark="MainPrice"], [data-name="Price"]')
              || [...e.querySelectorAll('[data-name="GeneralInfoSectionRowComponent"]')].map(x=>x.innerText.trim()).find(t=>/^[\d\s.,]+\s*₽$/.test(t)) || null;
            const unit = [...e.querySelectorAll('span,p')].filter(visible).map(x=>x.innerText.trim()).find(t=>/^[\d\s.,]+\s*₽\s*за\s+сотку$/.test(t)) || null;
            const stats = avito ? read(e,'[data-marker="seller-info/summary"],[class*="userInfoStep"] > span') : null;
            const badges = avito ? [...e.querySelectorAll('[data-marker*="badge-title"],[data-marker="item-badge"]')].filter(visible).map(x=>x.innerText.trim()).filter(Boolean) : [];
            const address = avito ? read(e,'[data-marker="item-address"],[data-marker="item-location"]') : null;
            const locationNodes = [...e.querySelectorAll(avito
              ? '[data-marker="item-address"],[data-marker="item-location"],[data-marker*="address"],[data-marker*="location"]'
              : '[data-name="GeoLabel"],[data-name*="Geo"],[data-name*="Location"]')].filter(visible).slice(0,12);
            const locationCandidates = locationNodes.map(x => {
              const marker=x.getAttribute('data-marker'), name=x.getAttribute('data-name');
              const label=marker ? 'data-marker='+marker : name ? 'data-name='+name : x.tagName.toLowerCase();
              const text=(x.innerText||x.textContent||'').replace(/\s+/g,' ').trim().slice(0,300);
              return text ? label+': '+text : null;
            }).filter(Boolean);
            const loc = avito ? address?.split('\n').filter(t=>!/(?:шоссе|МКАД).*\d+\s*км/i.test(t)).join(', ') || null
              : [...e.querySelectorAll('[data-name="GeoLabel"]')].filter(visible).map(x=>x.innerText.trim()).filter(Boolean).join(', ') || null;
            const transport = avito ? read(e,'[data-marker="item-transport"],[data-marker="item-address"] [class*="distance"]')
              || address?.split('\n').filter(t=>/(?:шоссе|МКАД).*\d+\s*км/i.test(t)).join('; ') || null
              : [...e.querySelectorAll('[data-name="SpecialGeo"],[data-name="TransportAccessibility"],[data-name="Highway"]')].filter(visible).map(x=>x.innerText.trim()).join('; ')||null;
            const sellerType = [...e.querySelectorAll('[data-name="BrandingLevelWrapper"] span, [data-name="AgentType"]')].filter(visible)
              .map(x=>x.innerText.trim()).find(t=>/^(Риелтор|Агентство|Собственник|Застройщик)$/i.test(t)) || null;
            return {Id:id||'',Url:a?.href||'',Title:read(e,avito?'[data-marker="item-title"],[data-marker="title"]':'[data-name="TitleComponent"]'),Price:price,UnitPrice:unit,
              Location:loc,Transport:transport,Description:description,Date:read(e,avito?'[data-marker="item-date"]':'[data-name="TimeLabel"]'),
              Seller:seller ? seller.innerText.trim() : read(e,avito?'[data-marker="seller-info/name"]':'[data-name="AgentName"]'),SellerUrl:seller?.href||null,SellerStatistics:stats,
              SellerType:sellerType,Badges:badges,Photos:[...e.querySelectorAll(avito?'a[data-marker="item-title"] img,[data-marker="item-photo"] img,[data-marker="item-gallery"] img,[class*="photo"] img':'[data-name="Gallery"] img')].map(x=>x.currentSrc||x.src).filter(Boolean),
              StructuredPrice:Number.isFinite(structured?.bargainTerms?.priceRur)?String(structured.bargainTerms.priceRur):null,
              StructuredArea:land?.area==null?null:String(land.area),StructuredAreaUnit:land?.areaUnitType||null,
              StructuredLocation:structured?.isNeedHideExactAddress===false&&typeof structured?.geo?.userInput==='string'&&structured.geo.userInput.trim()?structured.geo.userInput.trim():null,
              StructuredDescription:typeof structured?.description==='string'?structured.description:null,StructuredPhotos:structuredPhotos,
              CadastralNumber:typeof structured?.cadastralNumber==='string'&&structured.cadastralNumber.trim()?structured.cadastralNumber.trim():null,
              SourcePublishedUnix:Number.isSafeInteger(structured?.addedTimestamp)?structured.addedTimestamp:null,
              Latitude:Number.isFinite(coordinates?.lat)?coordinates.lat:null,Longitude:Number.isFinite(coordinates?.lng)?coordinates.lng:null,
              DeclaredLandTypes:declaredLandTypes,LocationCandidates:locationCandidates,
              StructuredLocationHidden:!avito&&typeof structured?.isNeedHideExactAddress==='boolean'?structured.isNeedHideExactAddress:null};
          });
          const loadingSelector='[data-marker*="loader"],[data-name="Loader"],[data-name="Loading"],[aria-busy="true"]';
          const loading = map ? [...main.querySelectorAll(loadingSelector)].some(visible) : has(loadingSelector);
          if (!map && !cards.length && !/ничего не найдено|нет объявлений|нет предложений/i.test(main.textContent)
              && !main.querySelector('[data-marker="items/list-empty"],[data-name="EmptyResults"]')) kind='Unknown';
          return {Kind:kind,Cards:cards,Warnings:[],Loading:loading,Layout:map?'AVITO_MAP':String(document.documentElement.scrollHeight),SourceCountHint:sourceCountHint};
        }
        """;
    public const string PaginationScript = """
        site => {
          const avito = site==='Avito';
          const rootSelector = avito ? '[data-marker="pagination"],[data-marker="pagination-button"]' : '[data-name="Pagination"]';
          const linkSelector = avito ? '[data-marker="pagination"] a,[data-marker="pagination-button"] a,a[data-marker="pagination-button/nextPage"],a[data-marker^="pagination-button/page("],a[rel="next"]' : '[data-name="Pagination"] a';
          const root = document.querySelector(rootSelector);
          const visible = e=>!!e.getClientRects().length;
          const current = Number(new URL(location.href).searchParams.get('p')||1);
          const links=[...document.querySelectorAll(linkSelector)].filter(e=>visible(e)&&e.getAttribute('aria-disabled')!=='true'&&!e.hasAttribute('disabled'));
          const declaresNext=e=>Number(e.textContent.trim())===current+1||/^(следующая|дальше|далее|next|›|→)$/i.test(e.textContent.trim())||e.rel==='next'||e.getAttribute('data-marker')==='pagination-button/nextPage';
          const pointsToNext=e=>{try{const u=new URL(e.href,location.href);return u.searchParams.getAll('p').length<=1&&Number(u.searchParams.get('p')||1)===current+1;}catch{return false;}};
          // A visible label may disagree with href. Select an actual n+1 link;
          // never skip a page or synthesize a navigation URL from a misleading label.
          const pagination=links.find(e=>pointsToNext(e)&&declaresNext(e))||links.find(pointsToNext);
          if(pagination) return {kind:'Next',url:pagination.href,selector:linkSelector};
          if(links.some(declaresNext)) return {kind:'UnknownInvalid',reason:'Номер следующей страницы в ссылке не совпадает с ожидаемым'};
          if(root) {
            const numbered = [...root.querySelectorAll('a,button,span')].filter(visible).map(e=>e.textContent.trim()).filter(t=>/^\d+$/.test(t)).map(Number);
            if(numbered.includes(current) && Math.max(...numbered)<=current) return {kind:'End',reason:'Последняя номерная страница'};
            const disabledNext=[...root.querySelectorAll('button,[aria-disabled="true"]')].find(e=>/следующая|дальше|next/i.test(e.textContent)&& (e.disabled||e.getAttribute('aria-disabled')==='true'));
            if(disabledNext) return {kind:'End',reason:'Следующая страница отключена'};
            return {kind:'UnknownInvalid',reason:'Пагинация есть, но следующий переход не распознан'};
          }
          const suspiciousRoots=[...document.querySelectorAll('[class*="pagination"],[data-name*="Pagination"],[data-marker*="pagination"]')].filter(visible);
          const paginationLike=suspiciousRoots.find(root => [...root.querySelectorAll('a,button,[role="button"]')].filter(visible).some(e => {
            const text=(e.textContent||'').trim();
            const aria=(e.getAttribute('aria-label')||'').trim();
            return /^\d+$/.test(text)
              || /^(следующая|дальше|далее|назад|предыдущая|next|prev|previous|›|‹|→|←)$/i.test(text)
              || /(следующ|предыдущ|next|prev)/i.test(aria)
              || e.rel==='next' || e.rel==='prev';
          }));
          return paginationLike ? {kind:'UnknownInvalid',reason:'Неизвестная разметка пагинации'} : {kind:'End',reason:'Основная выдача без пагинации'};
        }
        """;
}
