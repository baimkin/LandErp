using Microsoft.Playwright;

namespace LandErp.ParserSpike.LocalCollection;

/// <summary>One persistent browser context per site, with independently owned worker tabs and isolated profiles.</summary>
public sealed class BrowserSessions(string profileRoot, JsonResponseDiagnostics? jsonDiagnostics = null) : ISourceSessions
{
    private readonly SemaphoreSlim mutex = new(1, 1);
    private readonly Dictionary<SourceSite, IBrowserContext> contexts = [];
    private readonly Dictionary<SourceSite, FileStream> profileLocks = [];
    private readonly Dictionary<SourceSite, CollectionSettings> configurations = [];
    private readonly HashSet<IPage> leased = [];
    private IPlaywright? playwright;
    public async Task<ISourcePage> CreatePageAsync(SourceSite source, CollectionSettings settings, CancellationToken cancellationToken)
    {
        await mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (contexts.TryGetValue(source, out IBrowserContext? previous)
                && (configurations[source].Browser != settings.Browser
                    || configurations[source].NavigationTimeoutSeconds != settings.NavigationTimeoutSeconds
                    || configurations[source].ElementTimeoutSeconds != settings.ElementTimeoutSeconds
                    || previous.Pages.All(p => p.IsClosed)))
            {
                if (previous.Pages.Any(leased.Contains)) throw new InvalidOperationException("BROWSER_SETTINGS_BUSY");
                try { await previous.CloseAsync().ConfigureAwait(false); } catch (PlaywrightException) { }
                contexts.Remove(source); profileLocks[source].Dispose(); profileLocks.Remove(source);
            }
            if (!contexts.TryGetValue(source, out IBrowserContext? context))
            {
                playwright ??= await Playwright.CreateAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
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
                ?? await context.NewPageAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            leased.Add(page);
            return new DomSourcePage(page, source, async () =>
            {
                await mutex.WaitAsync().ConfigureAwait(false);
                try { leased.Remove(page); } finally { mutex.Release(); }
            }, settings.NavigationTimeoutSeconds, settings.MaxPages);
        }
        finally { mutex.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        try { foreach (IBrowserContext context in contexts.Values) try { await context.CloseAsync().ConfigureAwait(false); } catch (PlaywrightException) { /* Already closed manually. */ } }
        finally { contexts.Clear(); leased.Clear(); playwright?.Dispose(); playwright = null; foreach (FileStream handle in profileLocks.Values) handle.Dispose(); profileLocks.Clear(); mutex.Dispose(); }
    }
}
