using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using LandErp.ParserSpike.Contracts;

namespace LandErp.ParserSpike.LocalCollection;

public sealed record DiagnosticEvent(DateTimeOffset TimeUtc, string JobId, string BatchId, SourceSite Source,
    string Worker, int Page, string Action, string Outcome, long DurationMilliseconds,
    string ExpectedUrl, string ActualUrl, string? ErrorType, string Detail, int Count);

public sealed record DiagnosticError(string ErrorId, DateTimeOffset TimeUtc, string AppVersion, string InformationalVersion,
    string JobId, string BatchId, SourceSite Source, string Worker, int Page, string Action, string Reason,
    string? ErrorType, string CurrentUrl, string CandidateUrl, int Count, string? Screenshot,
    SourcePageDiagnostic? PageDiagnostic, DiagnosticEvent[] RecentEvents);

public sealed record DataQualityEvent(DateTimeOffset TimeUtc, string JobId, string BatchId, SourceSite Source,
    string Worker, int Page, string PageUrl, string ExternalId, string? Title, string[] MissingFields,
    Presence PricePresence, Presence AreaPresence, Presence LocationPresence,
    string? PriceRaw, string? AreaRaw, string? LocationRaw, string[] LocationCandidates,
    string? StructuredLocationCandidate, bool? StructuredLocationHidden,
    decimal? Latitude, decimal? Longitude, string Provenance, string AdapterVersion, string[] Warnings);

/// <summary>
/// Compact daily operational log plus rich, separate error diagnostics.
/// Legacy collection.jsonl is intentionally no longer appended to.
/// </summary>
public sealed class DiagnosticJournal
{
    private const int RecentPerJob = 30;
    private const int EventRetentionDays = 30;
    private const int ErrorRetentionDays = 90;
    private const int DataQualityRetentionDays = 30;
    private readonly object sync = new();
    private readonly Dictionary<string, Queue<DiagnosticEvent>> recent = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> lastDetailedError = new(StringComparer.Ordinal);
    private readonly HashSet<string> dataQualityWritten = new(StringComparer.Ordinal);
    public string RootPath { get; }
    public string Path => DailyPath("events", DateTimeOffset.Now, ".jsonl");
    public string ErrorsPath => DailyPath("errors", DateTimeOffset.Now, ".jsonl");
    public string DataQualityPath => DailyPath("data-quality", DateTimeOffset.Now, ".jsonl");

    public DiagnosticJournal(string path)
    {
        string full = System.IO.Path.GetFullPath(path);
        RootPath = System.IO.Path.GetExtension(full).Equals(".jsonl", StringComparison.OrdinalIgnoreCase)
            ? System.IO.Path.GetDirectoryName(full)!
            : full;
        Directory.CreateDirectory(RootPath);
        Directory.CreateDirectory(System.IO.Path.Combine(RootPath, "events"));
        Directory.CreateDirectory(System.IO.Path.Combine(RootPath, "errors"));
        Directory.CreateDirectory(System.IO.Path.Combine(RootPath, "data-quality"));
        Directory.CreateDirectory(System.IO.Path.Combine(RootPath, "attachments"));
        Cleanup();
    }

    public void Write(CollectionJob job, int page, string action, string outcome, long duration = 0,
        string? expectedUrl = null, string? actualUrl = null, string? errorType = null, string detail = "", int count = 0)
    {
        DiagnosticEvent entry = new(DateTimeOffset.UtcNow, job.Id, job.BatchId, job.Source, job.Owner ?? "", page,
            action, outcome, duration, SearchUrls.DiagnosticUrl(expectedUrl), SearchUrls.DiagnosticUrl(actualUrl),
            errorType, CompactDetail(detail), count);
        lock (sync)
        {
            Remember(entry);
            if (!ShouldPersist(entry)) return;
            Append(DailyPath("events", entry.TimeUtc.ToLocalTime(), ".jsonl"), entry);
        }
    }

    public async Task WriteErrorAsync(CollectionJob job, ISourcePage? page, int pageNumber, string action, string reason,
        string? errorType = null, string? candidateUrl = null, int count = 0, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string errorId = $"{now:yyyyMMddTHHmmssfff}-{Guid.NewGuid():N}";
        SourcePageDiagnostic? pageDiagnostic = null;
        string? screenshot = null;
        if (page is not null)
        {
            try { pageDiagnostic = await page.CaptureDiagnosticAsync(candidateUrl, cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) when (ex is TimeoutException or InvalidOperationException or Microsoft.Playwright.PlaywrightException) { }
            try
            {
                DateTimeOffset local = now.ToLocalTime();
                string directory = System.IO.Path.Combine(RootPath, "attachments", local.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                    local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                Directory.CreateDirectory(directory);
                string full = System.IO.Path.Combine(directory, errorId + ".png");
                if (await page.CaptureScreenshotAsync(full, cancellationToken).ConfigureAwait(false))
                    screenshot = System.IO.Path.GetRelativePath(RootPath, full);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException or Microsoft.Playwright.PlaywrightException) { }
        }

        DiagnosticEvent[] context;
        lock (sync)
        {
            context = recent.TryGetValue(job.Id, out Queue<DiagnosticEvent>? queue) ? queue.ToArray() : [];
            lastDetailedError[job.Id] = now;
        }
        string version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
        string info = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? version;
        DiagnosticError error = new(errorId, now, version, info, job.Id, job.BatchId, job.Source, job.Owner ?? "", pageNumber,
            action, CompactDetail(reason, 4000), errorType, SearchUrls.DiagnosticUrl(page?.CurrentUrl),
            SearchUrls.DiagnosticUrl(candidateUrl), count, screenshot, Sanitize(pageDiagnostic), context);
        lock (sync) Append(DailyPath("errors", now.ToLocalTime(), ".jsonl"), error);
    }

    public void WriteFailure(CollectionJob job, int page, string reason, string? errorType = null, int count = 0)
    {
        lock (sync)
        {
            if (lastDetailedError.TryGetValue(job.Id, out DateTimeOffset detailed)
                && DateTimeOffset.UtcNow - detailed < TimeSpan.FromSeconds(5)) return;
        }
        Write(job, page, "Задача", "Failed", errorType: errorType, detail: reason, count: count);
        DiagnosticError error;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        lock (sync)
        {
            DiagnosticEvent[] context = recent.TryGetValue(job.Id, out Queue<DiagnosticEvent>? queue) ? queue.ToArray() : [];
            string version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
            string info = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? version;
            error = new($"{now:yyyyMMddTHHmmssfff}-{Guid.NewGuid():N}", now, version, info, job.Id, job.BatchId,
                job.Source, job.Owner ?? "", page, "Задача", CompactDetail(reason, 4000), errorType, "", "", count, null, null, context);
            Append(DailyPath("errors", now.ToLocalTime(), ".jsonl"), error);
            lastDetailedError[job.Id] = now;
        }
    }

    public void WriteDataQuality(CollectionJob job, int page, string pageUrl,
        IEnumerable<ListingObservation> listings, IReadOnlyDictionary<string, ListingDataQualityDiagnostic> captured)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        lock (sync)
        {
            foreach (ListingObservation item in listings)
            {
                List<string> missing = [];
                if (item.Price.Parsed is null) missing.Add("Price");
                if (item.AreaSquareMeters.Parsed is null) missing.Add("Area");
                if (item.Location.Presence != Presence.Present || string.IsNullOrWhiteSpace(item.Location.Raw)) missing.Add("Location");
                if (missing.Count == 0) continue;

                string key = job.Id + "|" + page.ToString(CultureInfo.InvariantCulture) + "|" + item.ExternalId;
                if (!dataQualityWritten.Add(key)) continue;
                captured.TryGetValue(item.ExternalId, out ListingDataQualityDiagnostic? diagnostic);
                DataQualityEvent entry = new(now, job.Id, job.BatchId, job.Source, job.Owner ?? "", page,
                    SearchUrls.DiagnosticUrl(pageUrl), item.ExternalId, CompactDetail(item.Title.Raw, 300),
                    missing.ToArray(), item.Price.Presence, item.AreaSquareMeters.Presence, item.Location.Presence,
                    CompactDetail(item.Price.Raw, 300), CompactDetail(item.AreaSquareMeters.Raw, 300),
                    CompactDetail(item.Location.Raw, 500),
                    (diagnostic?.LocationCandidates ?? []).Where(value => !string.IsNullOrWhiteSpace(value))
                        .Select(value => CompactDetail(value, 500)).Take(12).ToArray(),
                    CompactDetail(diagnostic?.StructuredLocationCandidate, 500), diagnostic?.StructuredLocationHidden,
                    item.Latitude.Parsed, item.Longitude.Parsed, item.Provenance, item.AdapterVersion,
                    item.Warnings.Where(value => value.Length <= 80).Take(30).ToArray());
                Append(DailyPath("data-quality", now.ToLocalTime(), ".jsonl"), entry);
            }
        }
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
                tail.Enqueue(entry);
                if (tail.Count > Math.Clamp(limit, 1, 2000)) tail.Dequeue();
            }
        }
        return tail.Reverse().ToArray();
    }

    public void ExportToday(string destinationZip, string summary)
    {
        string temporary = destinationZip + ".tmp";
        if (File.Exists(temporary)) File.Delete(temporary);
        using (ZipArchive zip = ZipFile.Open(temporary, ZipArchiveMode.Create))
        {
            DateTimeOffset now = DateTimeOffset.Now;
            AddIfExists(zip, DailyPath("events", now, ".jsonl"), "events.jsonl");
            AddIfExists(zip, DailyPath("errors", now, ".jsonl"), "errors.jsonl");
            AddIfExists(zip, DailyPath("data-quality", now, ".jsonl"), "data-quality.jsonl");
            string attachments = System.IO.Path.Combine(RootPath, "attachments", now.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            if (Directory.Exists(attachments))
                foreach (string file in Directory.EnumerateFiles(attachments))
                    zip.CreateEntryFromFile(file, "attachments/" + System.IO.Path.GetFileName(file), CompressionLevel.Fastest);
            ZipArchiveEntry info = zip.CreateEntry("summary.txt", CompressionLevel.Fastest);
            using StreamWriter writer = new(info.Open(), new UTF8Encoding(false));
            writer.Write(summary);
        }
        File.Move(temporary, destinationZip, true);
    }

    public string Describe(DiagnosticEvent[] events) => "Файл: " + Path + Environment.NewLine + string.Join(Environment.NewLine, events.Select(e =>
        $"{e.TimeUtc.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)}  {e.Source}/{e.Worker}  стр. {e.Page}  {e.Action}: {e.Outcome} ({e.DurationMilliseconds} мс) {e.ErrorType} {e.Detail}{Environment.NewLine}  {e.ActualUrl}"));

    private void Remember(DiagnosticEvent entry)
    {
        if (!recent.TryGetValue(entry.JobId, out Queue<DiagnosticEvent>? queue))
            recent[entry.JobId] = queue = new();
        queue.Enqueue(entry);
        while (queue.Count > RecentPerJob) queue.Dequeue();
    }

    private static bool ShouldPersist(DiagnosticEvent entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.ErrorType)
            || entry.Outcome is "Ошибка" or "Failed" or "Failure" or "Unknown") return true;
        if (entry.Action == "Задача") return true;
        if (entry.Action == "Сохранение страницы" && entry.Outcome is "Завершённая" or "Владение утрачено") return true;
        if (entry.Action is "Переход на следующую страницу" or "Адрес после загрузки" or "Проверка адреса" or "Приостановка источника")
            return entry.Outcome != "Начато";
        if (entry.Action == "Резервирование ссылки") return true;
        return false;
    }

    private string DailyPath(string category, DateTimeOffset value, string extension)
    {
        string month = value.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        string day = value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string directory = System.IO.Path.Combine(RootPath, category, month);
        Directory.CreateDirectory(directory);
        return System.IO.Path.Combine(directory, day + extension);
    }

    private static void Append<T>(string path, T value)
    {
        string json = LocalJson.Write(value).Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal);
        File.AppendAllText(path, json + Environment.NewLine, Encoding.UTF8);
    }

    private static string CompactDetail(string? value, int maximum = 1500)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        string text = value.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
        return text.Length <= maximum ? text : text[..maximum] + "…";
    }

    private static SourcePageDiagnostic? Sanitize(SourcePageDiagnostic? value)
    {
        if (value is null) return null;
        return value with
        {
            CurrentUrl = SearchUrls.DiagnosticUrl(value.CurrentUrl),
            CandidateUrl = SearchUrls.DiagnosticUrl(value.CandidateUrl),
            PaginationElements = value.PaginationElements.Select(item => item with
            {
                Href = SearchUrls.DiagnosticUrl(item.Href),
                Text = CompactDetail(item.Text, 120),
                ClassName = CompactDetail(item.ClassName, 200)
            }).Take(30).ToArray()
        };
    }

    private static void AddIfExists(ZipArchive zip, string path, string name)
    { if (File.Exists(path)) zip.CreateEntryFromFile(path, name, CompressionLevel.Fastest); }

    private void Cleanup()
    {
        CleanupFiles(System.IO.Path.Combine(RootPath, "events"), EventRetentionDays);
        CleanupFiles(System.IO.Path.Combine(RootPath, "errors"), ErrorRetentionDays);
        CleanupFiles(System.IO.Path.Combine(RootPath, "data-quality"), DataQualityRetentionDays);
        CleanupFiles(System.IO.Path.Combine(RootPath, "attachments"), ErrorRetentionDays);
    }

    private static void CleanupFiles(string root, int days)
    {
        if (!Directory.Exists(root)) return;
        DateTime threshold = DateTime.UtcNow.AddDays(-days);
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            try { if (File.GetLastWriteTimeUtc(file) < threshold) File.Delete(file); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        foreach (string directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length))
            try { if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory); } catch (IOException) { }
    }
}

public sealed class CollectionActionException(string action, Exception inner) : InvalidOperationException(
    $"Действие «{action}»: {inner.GetType().Name}; подробности в диагностическом журнале", inner)
{
    public string Action { get; } = action;
}
