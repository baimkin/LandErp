using System.Globalization;
using System.Text;

namespace LandErp.ParserSpike.LocalCollection;

public sealed record DiagnosticEvent(DateTimeOffset TimeUtc, string JobId, string BatchId, SourceSite Source,
    string Worker, int Page, string Action, string Outcome, long DurationMilliseconds,
    string ExpectedUrl, string ActualUrl, string? ErrorType, string Detail, int Count);

/// <summary>Append-only operational events, separate from business observations; excludes HTML, headers and exception payloads.</summary>
public sealed class DiagnosticJournal
{
    private readonly object sync = new();
    public string Path { get; }
    public DiagnosticJournal(string path)
    { Path = System.IO.Path.GetFullPath(path); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!); }
    public void Write(CollectionJob job, int page, string action, string outcome, long duration = 0,
        string? expectedUrl = null, string? actualUrl = null, string? errorType = null, string detail = "", int count = 0)
    {
        DiagnosticEvent entry = new(DateTimeOffset.UtcNow, job.Id, job.BatchId, job.Source, job.Owner ?? "", page,
            action, outcome, duration, SearchUrls.DiagnosticUrl(expectedUrl), SearchUrls.DiagnosticUrl(actualUrl), errorType, detail, count);
        // No raw exception Message/ToString: Playwright call logs can contain arbitrary browser content.
        lock (sync) File.AppendAllText(Path, LocalJson.Write(entry).Replace("\r", "", StringComparison.Ordinal)
            .Replace("\n", "", StringComparison.Ordinal) + Environment.NewLine, Encoding.UTF8);
    }
    public DiagnosticEvent[] Read(string? jobId = null, int limit = 300)
    {
        Queue<DiagnosticEvent> tail = new();
        lock (sync)
        {
            if (!File.Exists(Path)) return [];
            foreach (string line in File.ReadLines(Path))
            {
                DiagnosticEvent entry = LocalJson.Read<DiagnosticEvent>(line);
                if (jobId is not null && entry.JobId != jobId) continue;
                tail.Enqueue(entry); if (tail.Count > Math.Clamp(limit, 1, 2000)) tail.Dequeue();
            }
        }
        return tail.Reverse().ToArray();
    }
    public string Describe(DiagnosticEvent[] events) => "Файл: " + Path + Environment.NewLine + string.Join(Environment.NewLine, events.Select(e =>
        $"{e.TimeUtc.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)}  {e.Source}/{e.Worker}  стр. {e.Page}  {e.Action}: {e.Outcome} ({e.DurationMilliseconds} мс) {e.ErrorType} {e.Detail}{Environment.NewLine}  {e.ActualUrl}"));
}

public sealed class CollectionActionException(string action, Exception inner) : InvalidOperationException(
    $"Действие «{action}»: {inner.GetType().Name}; подробности в диагностическом журнале", inner)
{
    public string Action { get; } = action;
}
