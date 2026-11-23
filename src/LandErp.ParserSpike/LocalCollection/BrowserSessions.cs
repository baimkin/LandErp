using Microsoft.Playwright;

namespace LandErp.ParserSpike.LocalCollection;

/// <summary>One persistent browser context per site, with independently owned worker tabs and isolated profiles.</summary>
public sealed class BrowserSessions(string profileRoot, JsonResponseDiagnostics? jsonDiagnostics = null,
    Func<Task<IPlaywright>>? playwrightFactory = null) : ISourceSessions
{
    private readonly SemaphoreSlim mutex = new(1, 1);
    private readonly Dictionary<SourceSite, IBrowserContext> contexts = [];
    private readonly Dictionary<SourceSite, FileStream> profileLocks = [];
    private readonly Dictionary<SourceSite, CollectionSettings> configurations = [];
    private readonly HashSet<IPage> leased = [];
    private readonly object healthSync = new();
    private readonly HashSet<SourceSite> interruptedSources = [];
    private IPlaywright? playwright;
    private bool disposed;
    public async Task<ISourcePage> CreatePageAsync(SourceSite source, CollectionSettings settings, CancellationToken cancellationToken)
    {
        await mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            bool interrupted; lock (healthSync) interrupted = interruptedSources.Contains(source);
            if (contexts.TryGetValue(source, out IBrowserContext? previous)
                && (interrupted || configurations[source].Browser != settings.Browser
                    || configurations[source].NavigationTimeoutSeconds != settings.NavigationTimeoutSeconds
                    || configurations[source].ElementTimeoutSeconds != settings.ElementTimeoutSeconds
                    || previous.Pages.All(p => p.IsClosed)))
            {
                if (previous.Pages.Any(leased.Contains)) throw new InvalidOperationException("BROWSER_SETTINGS_BUSY");
                try { await BrowserLifetime.CloseAsync(previous.CloseAsync()).ConfigureAwait(false); } catch (PlaywrightException) { }
                contexts.Remove(source); profileLocks[source].Dispose(); profileLocks.Remove(source);
                lock (healthSync) interruptedSources.Remove(source);
            }
            if (!contexts.TryGetValue(source, out IBrowserContext? context))
            {
                playwright ??= await BrowserLifetime.AcquireAsync(playwrightFactory?.Invoke() ?? Playwright.CreateAsync(),
                    value => { value.Dispose(); return Task.CompletedTask; }, cancellationToken).ConfigureAwait(false);
                // Avito keeps the original Gate 02 profile, so manual login survives this upgrade.
                string profile = System.IO.Path.Combine(profileRoot, source == SourceSite.Avito ? "spike-002" : "local-cian", settings.Browser);
                Directory.CreateDirectory(profile);
                profileLocks[source] = new FileStream(System.IO.Path.Combine(profile, ".landerplock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                try
                {
                    // Launch has its own finite timeout. Do not abandon an unobserved launch that could later hold the profile.
                    context = await playwright.Chromium.LaunchPersistentContextAsync(profile, new()
                    {
                        Channel = settings.Browser,
                        Headless = false,
                        ChromiumSandbox = true,
                        ViewportSize = ViewportSize.NoViewport,
                        Timeout = settings.NavigationTimeoutSeconds * 1000,
                        AcceptDownloads = false
                    }).ConfigureAwait(false);
                }
                catch { profileLocks[source].Dispose(); profileLocks.Remove(source); throw; }
                context.SetDefaultNavigationTimeout(settings.NavigationTimeoutSeconds * 1000);
                context.SetDefaultTimeout(settings.ElementTimeoutSeconds * 1000);
                if (jsonDiagnostics is not null) context.Response += (_, response) => jsonDiagnostics.Observe(response, source);
                contexts.Add(source, context);
                configurations[source] = settings;
                cancellationToken.ThrowIfCancellationRequested();
            }
            IPage page = context.Pages.FirstOrDefault(p => !p.IsClosed && !leased.Contains(p))
                ?? await BrowserLifetime.AcquireAsync(context.NewPageAsync(),
                    value => BrowserLifetime.CloseAsync(value.CloseAsync()), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            leased.Add(page);
            return new DomSourcePage(page, source, async () =>
            {
                if (disposed) return;
                await mutex.WaitAsync().ConfigureAwait(false);
                try { leased.Remove(page); } finally { mutex.Release(); }
            }, settings.NavigationTimeoutSeconds, settings.MaxPages,
                onInterrupted: () => { lock (healthSync) interruptedSources.Add(source); });
        }
        finally { mutex.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        await mutex.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed) return;
            disposed = true;
            // Close sites concurrently: one unresponsive site cannot multiply shutdown time.
            await Task.WhenAll(contexts.Values.Select(async context =>
            {
                try { await BrowserLifetime.CloseAsync(context.CloseAsync()).ConfigureAwait(false); }
                catch (Exception ex) when (ex is PlaywrightException or TimeoutException) { }
            })).ConfigureAwait(false);
        }
        finally
        {
            contexts.Clear(); leased.Clear(); configurations.Clear();
            lock (healthSync) interruptedSources.Clear();
            try { playwright?.Dispose(); }
            finally { playwright = null; foreach (FileStream handle in profileLocks.Values) handle.Dispose(); profileLocks.Clear(); mutex.Release(); }
        }
    }
}
