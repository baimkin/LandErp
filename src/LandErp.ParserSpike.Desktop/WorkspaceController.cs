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
    private ISourcePage? manualPage;
    private HttpClient? serverHttp;
    public ServerCoordinator? Server { get; private set; }
    public LocalStore Store { get; }
    public QueueRunner Runner { get; }
    public DiagnosticJournal Diagnostics { get; }
    public JsonResponseDiagnostics JsonResponses { get; }
    public WorkspaceController(string databasePath, string profileRoot, ISourceSessions? sessions = null)
    {
        guard = new InstanceGuard(databasePath);
        try
        {
            Store = new LocalStore(databasePath); Store.RecoverInterrupted();
            JsonResponses = new JsonResponseDiagnostics(Path.Combine(Path.GetDirectoryName(databasePath)!, "diagnostics", "browser-json"));
            this.sessions = sessions ?? new BrowserSessions(profileRoot, JsonResponses);
            Diagnostics = new DiagnosticJournal(Path.Combine(Path.GetDirectoryName(databasePath)!, "diagnostics", "collection.jsonl"));
            Runner = new QueueRunner(Store, this.sessions, Diagnostics);
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
        await CloseManualAsync();
        manualPage = await sessions.CreatePageAsync(link.Source, Store.Settings(), CancellationToken.None);
        await manualPage.OpenAsync(link.Url, CancellationToken.None);
        await manualPage.ActivateAsync(CancellationToken.None);
    }
    public async Task CloseManualAsync()
    { if (manualPage is not null) { await manualPage.DisposeAsync(); manualPage = null; } }
    public async Task ConnectServerAsync()
    {
        if (Server != null) return;
        ServerConnection connection = SavedServerConnection() ?? throw new InvalidOperationException("Нажмите «Подключить сервер» и заполните настройки.");
        await ConnectServerAsync(connection);
    }
    private string ServerSettingsPath => Path.Combine(Path.GetDirectoryName(Store.Path)!, "server-connection.json");
    internal ServerConnection? SavedServerConnection() => ServerConnectionSettings.Load(ServerSettingsPath);
    public async Task ConnectServerAsync(ServerConnection connection)
    {
        if (Runner.IsRunning) throw new InvalidOperationException("Сначала остановите или завершите текущий сбор.");
        ServerConnection? previous = SavedServerConnection();
        ServerOutbox outbox = new(Path.Combine(Path.GetDirectoryName(Store.Path)!, "collector-server-outbox.sqlite"));
        if (previous != null && (previous.AgentId != connection.AgentId || previous.Origin != connection.Origin)
            && (outbox.ReadWork() != null || outbox.Pending().Length > 0))
            throw new InvalidOperationException("Есть незавершённое задание или результаты для прежнего сервера. Сначала завершите их доставку.");
        HttpClient candidate = new() { Timeout = TimeSpan.FromSeconds(20) };
        try
        {
            ServerAdapter adapter = new(candidate, connection);
            await adapter.RegisterAsync(CancellationToken.None);
            ServerConnectionSettings.Save(ServerSettingsPath, connection);
            if (Server != null) await Server.DisposeAsync();
            serverHttp?.Dispose();
            Server = new(Store, Runner, outbox, adapter);
            serverHttp = candidate;
        }
        catch { candidate.Dispose(); throw; }
    }
    public async ValueTask DisposeAsync() { try { await CloseManualAsync(); await Runner.DisposeAsync(); if (Server != null) await Server.DisposeAsync(); serverHttp?.Dispose(); await JsonResponses.DisposeAsync(); } finally { guard.Dispose(); } }
}
