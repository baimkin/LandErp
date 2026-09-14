using LandErp.ParserSpike.Avito;
using System.Text.Json;
using Microsoft.Playwright;

namespace LandErp.ParserSpike.Browser;

/// <summary>One visible browser session; opening never triggers parsing.</summary>
public sealed class AvitoBrowser : IAsyncDisposable, Application.ISearchPage
{
    private IPlaywright? playwright;
    private IBrowserContext? context;
    private IPage? page;
    private Uri? requestedUrl;

    public AvitoBrowser() { }

    /// <summary>Uses a supplied page for deterministic browser integration tests.</summary>
    public AvitoBrowser(IPage targetPage)
    {
        ArgumentNullException.ThrowIfNull(targetPage);
        page = targetPage;
    }

    /// <summary>Whether an active browser page is available for an explicit parse action.</summary>
    public bool IsOpen => page is { IsClosed: false };

    /// <summary>Opens one user supplied URL, capped at three attempts per application session.</summary>
    public async Task OpenAsync(Uri url, string channel, string profileRoot)
    {
        if (!SearchParser.IsAvitoUrl(url)) throw new ArgumentException("AVITO_URL_REQUIRED", nameof(url));
        if (channel is not ("chrome" or "msedge")) throw new ArgumentException("INVALID_BROWSER", nameof(channel));
        if (!IsOpen)
        {
            if (context is not null) await context.CloseAsync().ConfigureAwait(false);
            playwright ??= await Playwright.CreateAsync().ConfigureAwait(false);
            context = await playwright.Chromium.LaunchPersistentContextAsync(
                Path.Combine(profileRoot, channel), new()
                {
                    Channel = channel,
                    Headless = false,
                    ChromiumSandbox = true,
                    ViewportSize = ViewportSize.NoViewport,
                    Timeout = 30000,
                    AcceptDownloads = false
                }).ConfigureAwait(false);
            page = context.Pages.Count > 0 ? context.Pages[0] : await context.NewPageAsync().ConfigureAwait(false);
            page.SetDefaultTimeout(10000);
        }
        requestedUrl = url;
        await page!.GotoAsync(url.AbsoluteUri, new()
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 30000
        }).ConfigureAwait(false);
        await page.BringToFrontAsync().ConfigureAwait(false);
    }

    /// <summary>Reads the current visible DOM only when called by the parse button.</summary>
    public async Task<SearchParseResult> ParseAsync()
    {
        if (!IsOpen) throw new InvalidOperationException("BROWSER_NOT_OPEN");
        Uri? finalUrl = Uri.TryCreate(page!.Url, UriKind.Absolute, out Uri? parsed) ? parsed : null;
        if (!SearchParser.IsAvitoUrl(finalUrl)) throw new InvalidOperationException("AVITO_URL_REQUIRED");
        // No HTML export, cookies, network headers or private session state are collected.
        SearchSnapshot snapshot = await ReadSnapshotAsync(page).ConfigureAwait(false);
        return SearchParser.Parse(snapshot, requestedUrl, finalUrl, DateTimeOffset.UtcNow);
    }

    public string CurrentUrl => page?.Url ?? "";

    public Task<SearchParseResult> ReadAsync() => ParseAsync();

    public async Task<bool> ScrollAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsOpen) throw new InvalidOperationException("BROWSER_NOT_OPEN");
        await page!.Mouse.WheelAsync(0, 480).ConfigureAwait(false);
        await Task.Delay(650, cancellationToken).ConfigureAwait(false);
        return await page.EvaluateAsync<bool>("window.scrollY + window.innerHeight >= document.documentElement.scrollHeight - 4").ConfigureAwait(false);
    }

    public async Task<string?> NextUrlAsync()
    {
        if (!IsOpen) throw new InvalidOperationException("BROWSER_NOT_OPEN");
        return await page!.EvaluateAsync<string?>("""
            () => {
              const a = [...document.querySelectorAll('a[data-marker="pagination-button/nextPage"], a[rel="next"]')]
                .find(e => e.getClientRects().length && e.getAttribute('aria-disabled') !== 'true');
              return a?.href || null;
            }
            """).ConfigureAwait(false);
    }

    public async Task GoNextAsync(string expectedUrl, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Uri.TryCreate(expectedUrl, UriKind.Absolute, out Uri? next) || !SearchParser.IsAvitoUrl(next))
            throw new InvalidOperationException("AVITO_URL_REQUIRED");
        if (await NextUrlAsync().ConfigureAwait(false) != expectedUrl)
            throw new InvalidOperationException("NEXT_PAGE_CHANGED");
        ILocator link = page!.Locator("a[data-marker='pagination-button/nextPage'], a[rel='next']")
            .Filter(new() { Visible = true });
        for (int i = 0; i < await link.CountAsync().ConfigureAwait(false); i++)
        {
            ILocator candidate = link.Nth(i);
            if (await candidate.EvaluateAsync<string>("e => e.href").ConfigureAwait(false) != expectedUrl) continue;
            await candidate.ClickAsync().ConfigureAwait(false);
            await page.WaitForURLAsync(expectedUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 }).ConfigureAwait(false);
            await Task.Delay(650, cancellationToken).ConfigureAwait(false);
            return;
        }
        throw new InvalidOperationException("NEXT_PAGE_CHANGED");
    }

    /// <summary>Closes only the dedicated research browser.</summary>
    public async ValueTask DisposeAsync()
    {
        IBrowserContext? closingContext = context;
        IPlaywright? closingPlaywright = playwright;
        context = null;
        playwright = null;
        page = null;
        try { if (closingContext is not null) await closingContext.CloseAsync().ConfigureAwait(false); }
        finally { closingPlaywright?.Dispose(); }
    }

    /// <summary>Transfers the selected DOM fields through JSON, without serializing the page HTML.</summary>
    public static async Task<SearchSnapshot> ReadSnapshotAsync(IPage targetPage)
    {
        string json = await targetPage.EvaluateAsync<string>("() => JSON.stringify((" + SnapshotScript + ")())").ConfigureAwait(false);
        return JsonSerializer.Deserialize<SearchSnapshot>(json) ?? throw new JsonException("Invalid DOM snapshot.");
    }

    /// <summary>Experimental Avito selectors isolated from orchestration and pure typing.</summary>
    public const string SnapshotScript = """
        () => {
          const visible = e => !!e && !!(e.offsetWidth || e.offsetHeight || e.getClientRects().length);
          const text = (root, selector) => {
            const e = [...root.querySelectorAll(selector)].find(visible);
            return e ? (e.innerText || '').trim().slice(0, 500) : '';
          };
          const unitPrice = root => text(root, '[data-marker="item-price-per-unit"], [data-marker="item-price-per-meter"]')
            || [...root.querySelectorAll('span, p')].filter(visible).map(e => (e.innerText || '').trim())
              .find(t => /^[\d\s.,₽]+\s*(?:₽\s*)?за\s+сотку\s*$/i.test(t)) || '';
          const heading = text(document, 'h1') + ' ' + document.title;
          const message = text(document, '[data-marker="error-page"], [data-marker="captcha"]') + ' ' + heading;
          const captcha = /капч|captcha|подтвердите,? что вы не робот|проверка.*браузер|доступ ограничен/i.test(message)
            || [...document.querySelectorAll('iframe[src*="captcha"], [data-marker="captcha"], #captcha')].some(visible);
          const auth = /вход|авторизац/i.test(heading)
            || [...document.querySelectorAll('input[type="password"]')].some(visible);
          const rate = /слишком много запросов|too many requests|429/i.test(message);
          const error = /сервис недоступен|ошибка сервера|страница не найдена|service unavailable/i.test(message);
          const details = !!document.querySelector('[data-marker="item-view/title-info"]');
          const main = document.querySelector('[data-marker="catalog-serp"]');
          const separators = main ? [...main.querySelectorAll('h2, h3, [data-marker*="recommend"]')]
            .filter(e => /других регион|других город|рекоменд|похожие|также/i.test(e.innerText || '')
              || /recommend/i.test(e.getAttribute('data-marker') || '')) : [];
          const cards = main ? [...main.querySelectorAll('[data-marker="item"]')]
            .filter(e => visible(e)
              && !e.closest('[data-marker*="recommend"], [data-marker*="advert"], [data-marker="item-ad"]')
              && !separators.some(s => !!(s.compareDocumentPosition(e) & Node.DOCUMENT_POSITION_FOLLOWING)))
            .map(e => {
              const a = e.querySelector('a[data-marker="item-title"]');
              const id = e.getAttribute('data-item-id') || (a?.href.match(/_(\d+)(?:\?|$)/)?.[1] || '');
              return { ExternalId: id, Url: a?.href || '', Title: text(e, '[data-marker="item-title"]'),
                Price: text(e, '[data-marker="item-price"]'),
                Location: text(e, '[data-marker="item-address"], [data-marker="item-location"]'),
                DateText: text(e, '[data-marker="item-date"]'),
                PricePerSotka: unitPrice(e),
                PreviewDescription: text(e, '[data-marker="item-description"]'),
                SellerName: text(e, '[data-marker="seller-info/name"], [data-marker="item-seller"], [data-marker="seller-link/link"]'),
                SellerInfo: text(e, '[data-marker="seller-info/summary"], [data-marker="seller-info/rating"], [data-marker="seller-info/description"]'),
                Badges: [...e.querySelectorAll('[data-marker*="badge"]')].filter(visible)
                  .map(b => (b.innerText || '').trim()).filter(Boolean).join('; ').slice(0, 500),
                PhotoUrl: e.querySelector('img')?.currentSrc || e.querySelector('img')?.src || '' };
            }) : [];
          return { Captcha: captcha, Authentication: auth, RateLimited: rate, SourceError: error,
            Details: details, HasMainResults: !!main, Cards: cards };
        }
        """;
}
