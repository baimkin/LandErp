using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using LandErp.ParserSpike.Contracts;
using Microsoft.Playwright;

namespace LandErp.ParserSpike.LocalCollection;

public sealed record DomCard(string Id, string Url, string? Title, string? Price, string? UnitPrice,
    string? Location, string? Transport, string? Description, string? Date, string? Seller,
    string? SellerUrl, string? SellerStatistics, string[] Badges, string[] Photos, string? SellerType = null);
public sealed record DomSnapshot(string Kind, DomCard[] Cards, string[] Warnings, bool Loading = false, string? Layout = null);

/// <summary>One worker owns one page. DOM selectors are source-specific; typing and orchestration are shared.</summary>
public sealed class DomSourcePage : ISourcePage
{
    private readonly IPage page;
    private readonly Func<Task>? release;
    private readonly int operationTimeoutSeconds;
    private readonly AvitoMapCapture? mapCapture;
    private bool mapLayout;
    public SourceSite Source { get; }
    public DomSourcePage(IPage page, SourceSite source, Func<Task>? release = null, int operationTimeoutSeconds = 30, int maximumMapBatches = 10)
    {
        this.page = page; this.release = release; this.operationTimeoutSeconds = operationTimeoutSeconds; Source = source;
        page.Response += OnResponse;
        if (source == SourceSite.Avito) mapCapture = new(page, maximumMapBatches);
    }
    private void OnResponse(object? sender, IResponse response)
    { if (response.Request.IsNavigationRequest && response.Request.Frame == page.MainFrame) httpStatus = response.Status; }
    public string CurrentUrl => page.Url;
    private int httpStatus;
    private bool interrupted;
    public async Task OpenAsync(string url, CancellationToken cancellationToken)
    {
        _ = SearchUrls.SafePage(url, Source);
        mapCapture?.Reset(url); mapLayout = false;
        IResponse? response;
        try { response = await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded }).WaitAsync(TimeSpan.FromSeconds(operationTimeoutSeconds), cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or PlaywrightException) { interrupted = true; throw; }
        httpStatus = response?.Status ?? 0;
    }
    public async Task<PageObservation> ReadAsync(CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(CurrentUrl, UriKind.Absolute, out Uri? uri) || !SearchUrls.IsPublic(uri, Source)) return new(PageKind.Unknown, [], ["UNEXPECTED_PAGE_URL"]);
        string json = await page.EvaluateAsync<string>("site => JSON.stringify((" + SnapshotScript + ")(site))", Source.ToString()).WaitAsync(TimeSpan.FromSeconds(operationTimeoutSeconds), cancellationToken).ConfigureAwait(false);
        DomSnapshot snapshot = JsonSerializer.Deserialize<DomSnapshot>(json) ?? throw new JsonException("DOM_SNAPSHOT_INVALID");
        if (httpStatus == 429) return new(PageKind.RateLimited, [], ["HTTP_429"]);
        if (httpStatus >= 400 && snapshot.Kind != "Captcha") return new(PageKind.SourceError, [], ["HTTP_" + httpStatus]);
        mapLayout = snapshot.Layout == "AVITO_MAP";
        if (mapLayout && snapshot.Kind == "SearchResults" && mapCapture is not null)
        {
            PageObservation result = mapCapture.Read(CurrentUrl, snapshot.Loading, Parse(snapshot, Source, DateTimeOffset.UtcNow).Listings);
            // Geometry is measured only; this diagnostic read never scrolls or changes collection.
            string state = await page.EvaluateAsync<string>("() => {try {const s=(" + MapScrollScript + ")();return JSON.stringify({top:s.scrollTop,client:s.clientHeight,height:s.scrollHeight});} catch {return JSON.stringify({error:'MAP_SCROLL_CONTAINER_UNKNOWN'});}}")
                .WaitAsync(TimeSpan.FromSeconds(operationTimeoutSeconds), cancellationToken).ConfigureAwait(false);
            using JsonDocument geometry = JsonDocument.Parse(state);
            JsonElement root = geometry.RootElement;
            return result with { Diagnostic = result.Diagnostic! with {
                ScrollTop = root.TryGetProperty("top",out JsonElement top) ? top.GetDouble() : null,
                ClientHeight = root.TryGetProperty("client",out JsonElement client) ? client.GetDouble() : null,
                ScrollHeight = root.TryGetProperty("height",out JsonElement height) ? height.GetDouble() : null,
                ScrollError = root.TryGetProperty("error",out JsonElement error) ? error.GetString() : null } };
        }
        return Parse(snapshot, Source, DateTimeOffset.UtcNow);
    }
    public static PageObservation Parse(DomSnapshot snapshot, SourceSite source, DateTimeOffset now)
    {
        if (!Enum.TryParse(snapshot.Kind, out PageKind kind)) kind = PageKind.Unknown;
        if (kind != PageKind.SearchResults) return new(kind, [], snapshot.Warnings);
        List<ListingObservation> items = []; List<string> errors = new(snapshot.Warnings); HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (DomCard card in snapshot.Cards)
        {
            if (!Regex.IsMatch(card.Id, @"^\d{5,20}$") || !Uri.TryCreate(card.Url, UriKind.Absolute, out Uri? url) || !SearchUrls.IsPublic(url, source)
                || !(source == SourceSite.Avito ? Regex.IsMatch(url.AbsolutePath, "_" + card.Id + "/?$") : Regex.IsMatch(url.AbsolutePath, "/sale/(?:suburban|flat)/" + card.Id + "/?$")))
            { errors.Add("INVALID_ID_OR_URL"); continue; }
            if (!ids.Add(card.Id)) continue;
            List<string> warnings = []; List<AreaAssertion> assertions = [];
            AddAreas(assertions, card.Title, "Title");
            // Description contributes only an explicit plot-area statement, never an arbitrary number near a road or house.
            foreach (Match match in Regex.Matches(card.Description ?? "", @"(?:площадь(?:\s+участка)?\s*[:—-]\s*|участок\s+)(\d+(?:[.,]\d+)?\s*(?:сот(?:ок|ки|ка|\.)?|га\b|м[²2]))", RegexOptions.IgnoreCase))
                AddAreas(assertions, match.Groups[1].Value, "Description");
            decimal? canonical = assertions.Count == 0 ? null : assertions[0].SquareMeters;
            if (assertions.Any(a => Math.Abs(a.SquareMeters - canonical!.Value) > 1)) { canonical = null; warnings.Add("AREA_CONFLICT"); }
            NumberValue price = NumberValue.Read(card.Price), unit = NumberValue.Read(card.UnitPrice);
            if (price.Presence != Presence.Present) warnings.Add("PRICE_" + price.Presence);
            if (assertions.Count == 0) warnings.Add("AREA_ABSENT");
            string? assignment = Regex.Match(card.Title ?? "", @"\b(?:ИЖС|СНТ|ДНП|ЛПХ)\b", RegexOptions.IgnoreCase) is { Success: true } use ? use.Value : null;
            string? statistics = card.SellerStatistics;
            Match completed = Regex.Match(statistics ?? "", @"(\d+)\s+заверш[её]н", RegexOptions.IgnoreCase);
            TextValue sellerType = TextValue.Read(card.SellerType ?? card.Badges.FirstOrDefault(x => Regex.IsMatch(x, "^(Собственник|Агент|Агентство|Застройщик|Риелтор)$", RegexOptions.IgnoreCase)));
            string[] photos = card.Photos.Where(x => Uri.TryCreate(x, UriKind.Absolute, out Uri? image) && image.Scheme == "https" && image.UserInfo.Length == 0
                && (image.Host.EndsWith(".avito.st", StringComparison.OrdinalIgnoreCase) || image.Host == "avito.st"
                || image.Host.EndsWith(".cdn-cian.ru", StringComparison.OrdinalIgnoreCase) || image.Host == "cdn-cian.ru"))
                .Select(x => new Uri(x).GetLeftPart(UriPartial.Path)).Distinct(StringComparer.Ordinal).ToArray();
            string? sellerUrl = null;
            if (Uri.TryCreate(card.SellerUrl, UriKind.Absolute, out Uri? seller) && SearchUrls.IsPublic(seller, source)
                && Regex.IsMatch(seller.AbsolutePath, @"^/(?:brands|user|agents|company|developers)/[a-zA-Z0-9_-]+/?$")) sellerUrl = seller.GetLeftPart(UriPartial.Path);
            string? description = card.Description;
            if (description?.Length > 100000) { description = description[..100000]; warnings.Add("DESCRIPTION_TRUNCATED_100000"); }
            items.Add(new()
            {
                Source = source,
                ExternalId = card.Id,
                Url = url.GetLeftPart(UriPartial.Path),
                ObservedAtUtc = now,
                Title = TextValue.Read(card.Title),
                Price = price,
                UnitPrice = unit,
                AreaSquareMeters = new(assertions.Count == 0 ? Presence.Absent : canonical.HasValue ? Presence.Present : Presence.ParseFailed,
                    assertions.Count == 0 ? null : string.Join("; ", assertions.Select(x => x.Raw)), canonical),
                Areas = assertions.ToArray(),
                DerivedPricePerSotka = canonical > 0 && price.Parsed.HasValue ? price.Parsed / (canonical / 100) : null,
                Assignment = TextValue.Read(assignment),
                Location = TextValue.Read(card.Location),
                Transport = TextValue.Read(card.Transport),
                Description = TextValue.Read(description),
                DateText = TextValue.Read(card.Date),
                SellerName = TextValue.Read(card.Seller),
                SellerUrl = TextValue.Read(sellerUrl),
                SellerStatistics = TextValue.Read(statistics),
                SellerType = sellerType,
                CompletedAdvertisements = completed.Success ? new(Presence.Present, statistics, decimal.Parse(completed.Groups[1].Value, CultureInfo.InvariantCulture)) : NumberValue.Read(null),
                Badges = card.Badges.Distinct(StringComparer.Ordinal).ToArray(),
                PhotoUrls = photos,
                Warnings = warnings.ToArray()
            });
        }
        return new(kind, items.ToArray(), errors.ToArray(), snapshot.Loading, snapshot.Layout);
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
    public async Task<bool> WheelAsync(CollectionSettings settings, CancellationToken cancellationToken)
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
    public async Task<Pagination> NextAsync(CancellationToken cancellationToken)
    {
        if (mapLayout) return new(NextKind.End, Reason: "Конец правого списка карты");
        string json = await page.EvaluateAsync<string>("site => JSON.stringify((" + PaginationScript + ")(site))", Source.ToString()).WaitAsync(TimeSpan.FromSeconds(operationTimeoutSeconds), cancellationToken).ConfigureAwait(false);
        Pagination pagination = LocalJson.Read<Pagination>(json);
        if (pagination.Kind != NextKind.Next || pagination.Url is null) return pagination;
        try
        {
            string safe = SearchUrls.SafePage(pagination.Url, Source);
            if (!SearchUrls.SameSearch(CurrentUrl, safe, Source)) return new(NextKind.UnknownInvalid, Reason: "Пагинация меняет фильтры поиска");
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
    public Task ActivateAsync(CancellationToken cancellationToken) => page.BringToFrontAsync().WaitAsync(TimeSpan.FromSeconds(operationTimeoutSeconds), cancellationToken);
    public async ValueTask DisposeAsync()
    {
        page.Response -= OnResponse;
        mapCapture?.Dispose();
        if (release is null || interrupted) await page.CloseAsync().ConfigureAwait(false);
        if (release is not null) await release().ConfigureAwait(false);
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
    public const string SnapshotScript = """
        site => {
          const avito = site === 'Avito';
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
            const seller = e.querySelector(avito?'[class*="userInfoStep"] a[href], a[data-marker="seller-link/link"], a[href*="/brands/"], a[href*="/user/"]':'a[href*="/agents/"],a[href*="/company/"],a[href*="/developers/"]');
            const description = read(e,avito?'[data-marker="item-description"], [class*="bottomBlock"] > [class*="ivaItemRedesign"] > p[style*="module-max-lines"], [class*="bottomBlock"] > div:first-child > p':'[data-name="Description"]',true);
            const price = avito ? read(e,'[data-marker="item-price"]') : read(e,'[data-mark="MainPrice"], [data-name="Price"]')
              || [...e.querySelectorAll('[data-name="GeneralInfoSectionRowComponent"]')].map(x=>x.innerText.trim()).find(t=>/^[\d\s.,]+\s*₽$/.test(t)) || null;
            const unit = [...e.querySelectorAll('span,p')].filter(visible).map(x=>x.innerText.trim()).find(t=>/^[\d\s.,]+\s*₽\s*за\s+сотку$/.test(t)) || null;
            const stats = avito ? read(e,'[data-marker="seller-info/summary"],[class*="userInfoStep"] > span') : null;
            const badges = [...e.querySelectorAll(avito?'[data-marker*="badge-title"],[data-marker="item-badge"]':'[data-name="FeatureLabels"], [data-testid*="badge"], [data-name="AgentType"]')].filter(visible).map(x=>x.innerText.trim()).filter(Boolean);
            const address = avito ? read(e,'[data-marker="item-address"],[data-marker="item-location"]') : null;
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
              SellerType:sellerType,Badges:badges,Photos:[...e.querySelectorAll(avito?'a[data-marker="item-title"] img,[data-marker="item-photo"] img,[data-marker="item-gallery"] img,[class*="photo"] img':'[data-name="Gallery"] img')].map(x=>x.currentSrc||x.src).filter(Boolean)};
          });
          const loadingSelector='[data-marker*="loader"],[data-name="Loader"],[data-name="Loading"],[aria-busy="true"]';
          const loading = map ? [...main.querySelectorAll(loadingSelector)].some(visible) : has(loadingSelector);
          if (!map && !cards.length && !/ничего не найдено|нет объявлений|нет предложений/i.test(main.textContent)
              && !main.querySelector('[data-marker="items/list-empty"],[data-name="EmptyResults"]')) kind='Unknown';
          return {Kind:kind,Cards:cards,Warnings:[],Loading:loading,Layout:map?'AVITO_MAP':String(document.documentElement.scrollHeight)};
        }
        """;
    public const string PaginationScript = """
        site => {
          const avito = site==='Avito';
          const rootSelector = avito ? '[data-marker="pagination"]' : '[data-name="Pagination"]';
          const linkSelector = avito ? '[data-marker="pagination"] a,a[data-marker="pagination-button/nextPage"],a[data-marker^="pagination-button/page("],a[rel="next"]' : '[data-name="Pagination"] a';
          const root = document.querySelector(rootSelector);
          const visible = e=>!!e.getClientRects().length;
          const current = Number(new URL(location.href).searchParams.get('p')||1);
          const links=[...document.querySelectorAll(linkSelector)].filter(e=>visible(e)&&e.getAttribute('aria-disabled')!=='true'&&!e.hasAttribute('disabled'));
          const pagination=links.find(e=>Number(e.textContent.trim())===current+1) || links.find(e=>/^(следующая|дальше|далее|next|›|→)$/i.test(e.textContent.trim())||e.rel==='next'||e.getAttribute('data-marker')==='pagination-button/nextPage');
          if(pagination) return {kind:'Next',url:pagination.href,selector:linkSelector};
          if(root) {
            const numbered = [...root.querySelectorAll('a,button,span')].filter(visible).map(e=>e.textContent.trim()).filter(t=>/^\d+$/.test(t)).map(Number);
            if(numbered.length && Math.max(...numbered)<=current) return {kind:'End',reason:'Последняя номерная страница'};
            const disabledNext=[...root.querySelectorAll('button,[aria-disabled="true"]')].find(e=>/следующая|дальше|next/i.test(e.textContent)&& (e.disabled||e.getAttribute('aria-disabled')==='true'));
            if(disabledNext) return {kind:'End',reason:'Следующая страница отключена'};
            return {kind:'UnknownInvalid',reason:'Пагинация есть, но следующий переход не распознан'};
          }
          const suspicious=document.querySelector('[class*="pagination"],[data-name*="Pagination"],[data-marker*="pagination"]');
          return suspicious ? {kind:'UnknownInvalid',reason:'Неизвестная разметка пагинации'} : {kind:'End',reason:'Основная выдача без пагинации'};
        }
        """;
}
