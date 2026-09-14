using System.IO;
using LandErp.ParserSpike.LocalCollection;

namespace LandErp.ParserSpike.Desktop;

/// <summary>Local application boundary: owns lifetime and commands, keeping SQL and browsers out of window handlers.</summary>
public sealed class WorkspaceController : IAsyncDisposable
{
    private readonly InstanceGuard guard;
    private readonly ISourceSessions sessions;
    private ISourcePage? manualPage;
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
    public async ValueTask DisposeAsync() { try { await CloseManualAsync(); await Runner.DisposeAsync(); await JsonResponses.DisposeAsync(); } finally { guard.Dispose(); } }
}
