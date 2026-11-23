using System.Text;
using System.Text.Json;

namespace LandErp.ParserSpike.LocalCollection;

public sealed record LastErrorDetails(DateTimeOffset TimeUtc, string JobId, SourceSite Source,
    string SearchUrl, string CurrentUrl, string Reason, string? ReasonCode, string CaptureStatus, string? HtmlFile,
    MapSelectionErrorEvidence? MapSelection = null);

public sealed record MapSelectionSnapshot(DateTimeOffset CapturedUtc, string Url, JsonElement State);
public sealed record MapSelectionErrorEvidence(MapSelectionSnapshot? LastRead, MapSelectionSnapshot? AtError, string? CaptureError);

/// <summary>One shared slot per Parser workspace. A slow older capture cannot replace a newer error.</summary>
public sealed class LastErrorSnapshot(string directory)
{
    public const int MaximumHtmlCharacters = 8 * 1024 * 1024;
    private readonly object publication = new();
    private long sequence;
    public string HtmlPath { get; } = Path.Combine(directory, "last-error.html");
    public string DetailsPath { get; } = Path.Combine(directory, "last-error.json");

    public async Task SaveAsync(CollectionJob job, ISourcePage? page, string reason, string? code)
    {
        long revision = Interlocked.Increment(ref sequence);
        DateTimeOffset time = DateTimeOffset.UtcNow;
        string? html = null;
        string currentUrl = "";
        string status = "Unavailable";
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(3));
        try
        {
            if (page is not null)
            {
                currentUrl = SearchUrls.DiagnosticUrl(page.CurrentUrl);
                html = await page.CaptureErrorHtmlAsync(deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);
                status = string.IsNullOrWhiteSpace(html) ? "Unavailable" : html.Length > MaximumHtmlCharacters ? "TooLarge" : "Saved";
                if (status != "Saved") html = null;
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
        { status = "Timeout"; }
        catch (Exception ex)
        { status = "CaptureFailed:" + ex.GetType().Name; }

        MapSelectionErrorEvidence? selection = null;
        if (page is not null)
        {
            using CancellationTokenSource selectionDeadline = new(TimeSpan.FromSeconds(3));
            try
            {
                selection = await page.CaptureMapSelectionErrorAsync(selectionDeadline.Token)
                    .WaitAsync(selectionDeadline.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            { selection = new(null, null, ex.GetType().Name); }
        }

        // Callers supply safe classification text, never raw exception payloads or HTTP headers.
        LastErrorDetails details = new(time, job.Id, job.Source, SearchUrls.DiagnosticUrl(job.Url), currentUrl,
            reason, code, status, html is null ? null : "last-error.html", selection);
        lock (publication)
        {
            try
            {
                if (revision != Volatile.Read(ref sequence)) return;
                Directory.CreateDirectory(directory);
                // Remove the previous pair first: unavailable/new failed writes must never leave misleading old HTML.
                File.Delete(DetailsPath);
                File.Delete(HtmlPath);
                if (html is not null)
                {
                    File.WriteAllText(HtmlPath + ".tmp", html, new UTF8Encoding(false));
                    File.Move(HtmlPath + ".tmp", HtmlPath, true);
                }
                File.WriteAllText(DetailsPath + ".tmp", LocalJson.Write(details), new UTF8Encoding(false));
                File.Move(DetailsPath + ".tmp", DetailsPath, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { /* Diagnostics cannot change collection outcome or prevent observations delivery. */ }
            finally
            {
                foreach (string temporary in new[] { HtmlPath + ".tmp", DetailsPath + ".tmp" })
                    try { File.Delete(temporary); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
    }
}
