using System.Globalization;
using System.IO;
using System.Net.Http;
using LandErp.ParserSpike.LocalCollection;
using LandErp.ParserSpike.ServerIntegration;

namespace LandErp.ParserSpike.Desktop;

/// <summary>Local application boundary: owns lifetime and commands, keeping SQL and browsers out of window handlers.</summary>
public sealed class WorkspaceController : IAsyncDisposable
{
    private readonly InstanceGuard guard;
    private readonly ISourceSessions sessions;
    private readonly Func<HttpClient> httpClientFactory;
    private ISourcePage? manualPage;
    private HttpClient? serverHttp;
    private readonly SemaphoreSlim localScheduler = new(1, 1);
    private readonly SemaphoreSlim serverConnection = new(1, 1);
    private DateTimeOffset reconnectAt;
    public bool AutomationEnabled { get; private set; }
    public bool SuspendNewWork { get; set; }
    public string ConnectionStatus { get; private set; } = "Сервер не подключён";
    public ParserOperatingMode Mode { get; private set; }
    public ListingDetailsDisplayMode ListingDetailsMode { get; private set; }
    public double ListingDetailsWidth { get; private set; }
    public ServerCoordinator? Server { get; private set; }
    public LocalStore Store { get; }
    public QueueRunner Runner { get; }
    public DiagnosticJournal Diagnostics { get; }
    public WorkspaceController(string databasePath, string profileRoot, ISourceSessions? sessions = null, Func<HttpClient>? httpClientFactory = null)
    {
        this.httpClientFactory = httpClientFactory ?? (() => new HttpClient { Timeout = TimeSpan.FromSeconds(20) });
        guard = new InstanceGuard(databasePath);
        try
        {
            Store = new LocalStore(databasePath); Store.RecoverInterrupted();
            this.sessions = sessions ?? new BrowserSessions(profileRoot);
            Diagnostics = new DiagnosticJournal(Path.Combine(Path.GetDirectoryName(databasePath)!, "diagnostics", "collection.jsonl"));
            Runner = new QueueRunner(Store, this.sessions, Diagnostics);
            Mode = LoadMode();
            ListingDetailsMode = LoadListingDetailsMode();
            ListingDetailsWidth = LoadListingDetailsWidth();
            AutomationEnabled = File.Exists(AutomationPath) && File.ReadAllText(AutomationPath).Trim() == "on";
        }
        catch { guard.Dispose(); throw; }
    }
    public static string WorkspaceRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LandErp.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LandErp", "ParserSpike");
    }
    public async Task OpenManualAsync(SearchLink link)
    {
        if (Runner.IsRunning) throw new InvalidOperationException("Сначала остановите текущий сбор.");
        if (Server != null) Server.AcceptNewWork = false;
        await CloseManualAsync();
        manualPage = await sessions.CreatePageAsync(link.Source, Store.Settings(), CancellationToken.None);
        await manualPage.OpenAsync(link.Url, CancellationToken.None);
        await manualPage.ActivateAsync(CancellationToken.None);
    }
    public async Task CloseManualAsync()
    { if (manualPage is not null) { await manualPage.DisposeAsync(); manualPage = null; } }
    public (SourceSite Source, string Url)? CurrentManualSearch() => manualPage is null ? null : (manualPage.Source, manualPage.CurrentUrl);
    private string AutomationPath => Path.Combine(Path.GetDirectoryName(Store.Path)!, "automation.txt");
    public void SetAutomation(bool enabled)
    {
        AutomationEnabled = enabled;
        if (Server != null) Server.AcceptNewWork = enabled && manualPage == null;
        if (!enabled) Runner.Stop();
        string temporary = AutomationPath + ".tmp";
        File.WriteAllText(temporary, enabled ? "on" : "off"); File.Move(temporary, AutomationPath, true);
    }
    public async Task TickAsync(CancellationToken token)
    {
        if (Mode == ParserOperatingMode.Local) { await TickLocalScheduleAsync(token); return; }
        if (Server == null && DateTimeOffset.UtcNow >= reconnectAt && SavedServerConnection() != null)
        {
            reconnectAt = DateTimeOffset.UtcNow.AddSeconds(30);
            try { await ConnectServerAsync(); ConnectionStatus = "Сервер подключён"; }
            catch (Exception ex) when (ex is ServerDeliveryException or InvalidOperationException)
            { ConnectionStatus = "Нет связи. Повторное подключение через 30 секунд."; }
        }
        if (Server != null) { Server.AcceptNewWork = AutomationEnabled && !SuspendNewWork && manualPage == null; await Server.TickAsync(token); ConnectionStatus = Server.Status; }
    }
    private string ModeSettingsPath => Path.Combine(Path.GetDirectoryName(Store.Path)!, "workspace-mode.txt");
    private string ListingDetailsModePath => Path.Combine(Path.GetDirectoryName(Store.Path)!, "listing-details-mode.txt");
    private string ListingDetailsWidthPath => Path.Combine(Path.GetDirectoryName(Store.Path)!, "listing-details-width.txt");
    private ParserOperatingMode LoadMode() => File.Exists(ModeSettingsPath)
        && Enum.TryParse(File.ReadAllText(ModeSettingsPath).Trim(), out ParserOperatingMode value) ? value : ParserOperatingMode.Local;
    private ListingDetailsDisplayMode LoadListingDetailsMode() => File.Exists(ListingDetailsModePath)
        && Enum.TryParse(File.ReadAllText(ListingDetailsModePath).Trim(), out ListingDetailsDisplayMode value)
        ? value : ListingDetailsDisplayMode.SidePanel;
    private double LoadListingDetailsWidth()
    {
        if (!File.Exists(ListingDetailsWidthPath)) return 430d;
        return double.TryParse(File.ReadAllText(ListingDetailsWidthPath).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            && double.IsFinite(value) ? Math.Clamp(value, 320d, 760d) : 430d;
    }
    public void SetListingDetailsWidth(double width)
    {
        if (!double.IsFinite(width)) return;
        double value = Math.Clamp(width, 320d, 760d);
        string temporary = ListingDetailsWidthPath + ".tmp";
        File.WriteAllText(temporary, value.ToString("0", CultureInfo.InvariantCulture));
        File.Move(temporary, ListingDetailsWidthPath, true);
        ListingDetailsWidth = value;
    }
    public void SetListingDetailsMode(ListingDetailsDisplayMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        string temporary = ListingDetailsModePath + ".tmp";
        File.WriteAllText(temporary, mode.ToString()); File.Move(temporary, ListingDetailsModePath, true);
        ListingDetailsMode = mode;
    }
    public void SetMode(ParserOperatingMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (Runner.IsRunning || Server?.CurrentWork != null) throw new InvalidOperationException("Завершите или остановите текущую работу перед переключением режима.");
        if (mode != Mode) SetAutomation(false);
        string temporary = ModeSettingsPath + ".tmp";
        File.WriteAllText(temporary, mode.ToString()); File.Move(temporary, ModeSettingsPath, true); Mode = mode;
    }
    public async Task TickLocalScheduleAsync(CancellationToken token)
    {
        if (!AutomationEnabled || SuspendNewWork || manualPage != null || Mode != ParserOperatingMode.Local || Runner.IsRunning || !await localScheduler.WaitAsync(0, token).ConfigureAwait(false)) return;
        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            LocalScheduledLink? due = Store.DueLinks(now).FirstOrDefault();
            if (due == null) return;
            _ = Runner.StartAsync(Store.Settings(), force: true, onlyLinkId: due.Link.Id);
            Store.MarkScheduleDispatched(due.Link.Id, due.Revision, now);
        }
        finally { localScheduler.Release(); }
    }
    public async Task ConnectServerAsync()
    {
        await serverConnection.WaitAsync();
        try
        {
            if (Server != null) return;
            ServerConnection connection = SavedServerConnection() ?? throw new InvalidOperationException("Откройте настройки и подключите сервер.");
            await ConnectServerAsync(connection);
        }
        finally { serverConnection.Release(); }
    }
    private string ServerSettingsPath => Path.Combine(Path.GetDirectoryName(Store.Path)!, "server-connection.json");
    internal ServerConnection? SavedServerConnection() => ServerConnectionSettings.Load(ServerSettingsPath);
    public async Task ConnectCodeAsync(string code)
    {
        if (!code.StartsWith("LDP1.", StringComparison.Ordinal)) { await ConnectServerAsync(ServerConnection.FromConnectionCode(code)); return; }
        if (Runner.IsRunning) throw new InvalidOperationException("Сначала остановите текущий сбор.");
        ServerConnection activation = ServerAdapter.ParseActivationCode(code);
        ServerOutbox outbox = new(Path.Combine(Path.GetDirectoryName(Store.Path)!, "collector-server-outbox.sqlite"));
        if (outbox.ReadWork() != null || outbox.Pending().Length > 0)
            throw new InvalidOperationException("Сначала завершите доставку прежнего задания.");
        using HttpClient client = httpClientFactory();
        ServerConnection connection = await new ServerAdapter(client, activation).ActivateAsync(CancellationToken.None);
        // The one-time code is consumed. Persist the issued credential before any retryable registration call.
        ServerConnectionSettings.Save(ServerSettingsPath, connection);
        await ConnectServerAsync(connection);
    }
    public async Task ConnectServerAsync(ServerConnection connection)
    {
        if (Runner.IsRunning) throw new InvalidOperationException("Сначала остановите или завершите текущий сбор.");
        ServerConnection? previous = SavedServerConnection();
        ServerOutbox outbox = new(Path.Combine(Path.GetDirectoryName(Store.Path)!, "collector-server-outbox.sqlite"));
        if (previous != null && (previous.AgentId != connection.AgentId || previous.Origin != connection.Origin)
            && (outbox.ReadWork() != null || outbox.Pending().Length > 0))
            throw new InvalidOperationException("Есть незавершённое задание или результаты для прежнего сервера. Сначала завершите их доставку.");
        outbox.Bind(connection, allowUnboundData: previous != null);
        HttpClient candidate = httpClientFactory();
        try
        {
            ServerAdapter adapter = new(candidate, connection);
            await adapter.RegisterAsync(CancellationToken.None);
            ServerConnectionSettings.Save(ServerSettingsPath, connection);
            if (Server != null) await Server.DisposeAsync();
            serverHttp?.Dispose();
            Server = new(Store, Runner, outbox, adapter);
            Server.AcceptNewWork = AutomationEnabled;
            serverHttp = candidate;
        }
        catch { candidate.Dispose(); throw; }
    }
    public async ValueTask DisposeAsync() { try { await CloseManualAsync(); await Runner.DisposeAsync(); if (Server != null) await Server.DisposeAsync(); serverHttp?.Dispose(); localScheduler.Dispose(); serverConnection.Dispose(); } finally { guard.Dispose(); } }
}

public enum ParserOperatingMode { Local, Server }
public enum ListingDetailsDisplayMode { SidePanel, SeparateWindow }
