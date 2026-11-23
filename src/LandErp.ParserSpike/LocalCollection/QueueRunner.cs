using System.Diagnostics;
using LandErp.Collector.Contracts.V1;
using Microsoft.Data.Sqlite;
using Microsoft.Playwright;

namespace LandErp.ParserSpike.LocalCollection;

public sealed record CollectionNotice(SourceSite Source, string JobId, string Link, string Worker, string Reason);

/// <summary>Claims belong to one worker; manual protection gates and navigation budgets are shared per source.</summary>
public sealed class QueueRunner(LocalStore store, ISourceSessions sessions, DiagnosticJournal? diagnostics = null) : IAsyncDisposable
{
    public SearchAreaStore SearchAreas { get; } = new(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(store.Path)!, "search-areas"));
    private sealed class SourceControl : IDisposable
    {
        public readonly SemaphoreSlim Navigation = new(1, 1);
        public DateTimeOffset NextNavigation;
        public TaskCompletionSource Ready = Signal(true);
        public bool Blocked;
        public readonly HashSet<string> VerificationJobs = [];
        public void Dispose() => Navigation.Dispose();
    }
    private sealed record ActiveWorker(CollectionJob Job, ISourcePage Page, string Name);
    private readonly Dictionary<SourceSite, SourceControl> sources = new() { [SourceSite.Avito] = new(), [SourceSite.Cian] = new() };
    private readonly Dictionary<string, ActiveWorker> active = [];
    private readonly object sync = new();
    private CancellationTokenSource? cancellation;
    private TaskCompletionSource userReady = Signal(true);
    private bool userPaused;
    private bool serverManaged;
    private string? batch;
    private volatile bool isRunning;
    private bool disposed;
    private long stopVersion;
    public long StopVersion => Volatile.Read(ref stopVersion);
    public bool IsRunning { get => isRunning; private set => isRunning = value; }
    public string? LastError { get; private set; }
    public event EventHandler? Changed;
    public event EventHandler<CollectionNotice>? ManualActionRequired;
    public Task? Completion { get; private set; }
    public string? BatchId => batch;
    private static TaskCompletionSource Signal(bool ready)
    { TaskCompletionSource signal = new(TaskCreationOptions.RunContinuationsAsynchronously); if (ready) signal.SetResult(); return signal; }

    public Task StartAsync(CollectionSettings settings, bool force = false, string? onlyLinkId = null, bool serverManaged = false, long? expectedStopVersion = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        settings.Validate();
        long requestedBeforeStop = expectedStopVersion ?? StopVersion;
        lock (sync)
        {
            if (IsRunning) throw new InvalidOperationException("Сбор уже выполняется.");
            LastError = null; this.serverManaged = serverManaged; cancellation?.Dispose(); cancellation = new(); IsRunning = true;
            // Stop can happen while a UI/server launch waits on a background read or SQLite.
            // Fence that already-requested launch, even if it has not assigned its CTS yet.
            if (requestedBeforeStop != StopVersion) cancellation.Cancel();
            userPaused = false; userReady = Signal(true);
            foreach (SourceControl source in sources.Values) { source.Blocked = false; source.VerificationJobs.Clear(); source.Ready = Signal(true); }
        }
        try { batch = store.StartBatch(settings, force, onlyLinkId: onlyLinkId); }
        catch { IsRunning = false; throw; }
        Completion = RunAsync(batch, settings, cancellation.Token);
        return Completion;
    }
    private async Task RunAsync(string batchId, CollectionSettings settings, CancellationToken token)
    {
        try
        {
            await Task.Run(async () =>
            {
                using SemaphoreSlim globalSlots = new(settings.GlobalTabs, settings.GlobalTabs);
                List<Task> workers = [];
                for (int index = 0; index < 3; index++)
                    foreach (SourceSite source in Enum.GetValues<SourceSite>())
                        if (index < (source == SourceSite.Avito ? settings.AvitoTabs : settings.CianTabs))
                            workers.Add(LimitedWorkerAsync(globalSlots, batchId, source, $"{source} {index + 1}", settings, token));
                await Task.WhenAll(workers).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            // Every launch, including scheduled/server work, owns and observes its completion.
            // Exception payloads may contain page/session data: persist only the safe type.
            LastError = "Сбор завершился с ошибкой " + ex.GetType().Name + ". Частичные результаты сохранены.";
            try { store.FailBatch(batchId, LastError); }
            catch (SqliteException) { /* The UI retains the error even when storage is unavailable. */ }
        }
        finally
        {
            try { store.StopBatch(batchId); }
            catch (Exception ex) { LastError = "Не удалось завершить запись состояния: " + ex.GetType().Name; }
            finally { IsRunning = false; Changed?.Invoke(this, EventArgs.Empty); }
        }
    }
    private async Task LimitedWorkerAsync(SemaphoreSlim slots, string batchId, SourceSite source, string name, CollectionSettings settings, CancellationToken token)
    {
        await slots.WaitAsync(token).ConfigureAwait(false);
        try { await WorkerAsync(batchId, source, name, settings, token).ConfigureAwait(false); }
        catch { cancellation?.Cancel(); throw; }
        finally { slots.Release(); }
    }
    private async Task WorkerAsync(string batchId, SourceSite source, string name, CollectionSettings settings, CancellationToken token)
    {
        ISourcePage? page = null;
        try
        {
            while (true)
            {
                await ReadyAsync(source, token).ConfigureAwait(false);
                CollectionJob? job;
                lock (sync) job = sources[source].Blocked || userPaused ? null : store.Claim(batchId, source, name);
                if (job is null)
                {
                    bool blocked; lock (sync) blocked = sources[source].Blocked || userPaused;
                    if (blocked) continue;
                    break;
                }
                Changed?.Invoke(this, EventArgs.Empty);
                diagnostics?.Write(job, job.Page, "Резервирование ссылки", "Начато", expectedUrl: job.Url);
                try
                {
                    page ??= await ActionAsync(job, null, job.Page, "Открытие вкладки", () => sessions.CreatePageAsync(source, settings, token), token).ConfigureAwait(false);
                    lock (sync) active[name] = new(job, page, name);
                    await NavigateAsync(source, settings.LinkIntervalSeconds, () => ActionAsync(job, page, job.Page, "Загрузка ссылки", async () => { await page.OpenAsync(job.Url, token).ConfigureAwait(false); return true; }, token, job.Url), token).ConfigureAwait(false);
                    if (page.CurrentUrl != job.Url) diagnostics?.Write(job, job.Page, "Адрес после загрузки", "Преобразован сайтом", expectedUrl: job.Url, actualUrl: page.CurrentUrl);
                    await CollectAsync(job, page, settings, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (SqliteException) { cancellation?.Cancel(); throw; }
                catch (Exception ex)
                {
                    LastError = "Ошибка сбора: " + ex.GetType().Name + ". Частичные результаты сохранены.";
                    string code = ExceptionReason(ex);
                    await FinishAsync(job, page, JobState.Failed, CollectionCompletionKind.SourceError, code,
                        (ex is CollectionActionException ? ex.Message : "Ошибка " + ex.GetType().Name) + "; результаты до ошибки сохранены",
                        [], false, false, 0);
                    if (settings.ErrorPolicy == ErrorPolicy.PauseSource)
                    {
                        Block(job, name, "Ошибка источника: проверьте вкладку");
                        await ReadyAsync(source, token).ConfigureAwait(false);
                    }
                    else if (page is not null) { try { await page.DisposeAsync().ConfigureAwait(false); } catch (Exception cleanup) when (cleanup is PlaywrightException or TimeoutException) { } page = null; }
                }
                finally
                {
                    lock (sync) active.Remove(name);
                    CollectionJob? finished = store.Job(job.Id);
                    diagnostics?.Write(job, finished?.Page ?? job.Page, "Задача", finished?.State.ToString() ?? "Завершена", actualUrl: page?.CurrentUrl,
                        detail: finished?.Reason ?? "", count: store.Journal(job.Id).Sum(p => p.Count));
                    diagnostics?.ForgetJob(job.Id);
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        finally { if (page is not null) try { await page.DisposeAsync().ConfigureAwait(false); } catch (Exception cleanup) when (cleanup is PlaywrightException or TimeoutException) { } }
    }
    private async Task CollectAsync(CollectionJob job, ISourcePage page, CollectionSettings settings, CancellationToken token)
    {
        int number = job.Page;
        string effectiveSearch = page.CurrentUrl;
        HashSet<string> visited = new(StringComparer.Ordinal) { page.CurrentUrl };
        HashSet<string> jobWarnings = new(StringComparer.Ordinal);
        HashSet<string> jobSeen = new(StringComparer.Ordinal);
        int? sourceCountHint = null;
        // A drawn list is confirmed by its source-declared area reference below.
        // Only an observed map activates polygon/coordinate requirements; drawId is not a layout.
        SearchAreaGuard? areaGuard = null;
        try
        {
        while (number <= job.Limit)
        {
            Dictionary<string, ListingObservation> gathered = new(StringComparer.Ordinal);
            Dictionary<string, ListingDataQualityDiagnostic> qualityDiagnostics = new(StringComparer.Ordinal);
            HashSet<string> pageWarnings = new(StringComparer.Ordinal);
            MapScope? map = null;
            string? lastMapDiagnostic = null;
            bool settled = false, lastLoading = true; int stableRounds = 0;
            Stopwatch stable = Stopwatch.StartNew(), initialReady = Stopwatch.StartNew();
            DateTimeOffset? loadingSince = null;
            int loadingTimeoutSeconds = Math.Max(15, settings.LoadWaitSeconds);
            for (int step = 0; step < settings.MaxScrollSteps; step++)
            {
                await ReadyAsync(job.Source, token).ConfigureAwait(false);
                PageObservation snapshot = await ActionAsync(job, page, number, "Чтение выдачи", () => page.ReadAsync(token), token).ConfigureAwait(false);
                if (areaGuard is null && job.Source == SourceSite.Avito && snapshot.Map is not null)
                    areaGuard = new(SearchAreas, job.Url, serverManaged, job.Id);
                if (areaGuard is not null && snapshot.Kind == PageKind.SearchResults && !snapshot.Loading && snapshot.Map is null)
                {
                    await FinishAsync(job, page, JobState.Failed, CollectionCompletionKind.SourceError, CollectionResultReasonCodes.LayoutChanged,
                        "Карта сменилась списком во время сбора; проверенные данные сохранены", [], false, true, stableRounds); return;
                }
                if (areaGuard is not null) snapshot = areaGuard.Apply(snapshot, page.CurrentUrl);
                foreach (ListingDataQualityDiagnostic item in snapshot.DataQuality ?? [])
                    qualityDiagnostics[item.ExternalId] = item;
                if (snapshot.SourceCountHint is int hint)
                {
                    if (sourceCountHint is int previous && previous != hint) jobWarnings.Add("SOURCE_COUNT_HINT_CHANGED");
                    sourceCountHint = hint;
                }
                if (snapshot.Diagnostic is not null)
                {
                    string detail = MapDiagnosticSummary(snapshot.Diagnostic);
                    if (detail != lastMapDiagnostic) diagnostics?.Write(job, number, "Диагностика карты", "Снимок", detail: detail, count: snapshot.Listings.Length);
                    lastMapDiagnostic = detail;
                }
                lastLoading = snapshot.Loading;
                if (snapshot.Kind == PageKind.RateLimited && serverManaged)
                {
                    await FinishAsync(job, page, JobState.Failed, CollectionCompletionKind.RateLimited, "", "Источник временно ограничил запросы",
                        snapshot.Warnings, false, false, stableRounds); return;
                }
                if (snapshot.Kind is PageKind.Captcha or PageKind.AuthenticationRequired or PageKind.RateLimited)
                {
                    Block(job, job.Owner!, snapshot.Kind.ToString());
                    await ReadyAsync(job.Source, token).ConfigureAwait(false);
                    stable.Restart(); initialReady.Restart(); continue;
                }
                if (snapshot.AreaCheck is { } area)
                {
                    foreach (string warning in snapshot.Warnings) jobWarnings.Add(warning);
                    if (!area.FiltersMatch)
                    {
                        await FinishAsync(job, page, JobState.Failed, CollectionCompletionKind.SourceError, CollectionResultReasonCodes.InvalidSearchUrl,
                            "Изменились условия поиска; эталон области сохранён", ["AREA_FILTERS_CHANGED"], false, false, stableRounds); return;
                    }
                    if (!area.GeometryMatches)
                    {
                        foreach (ListingObservation item in snapshot.Listings) { gathered[item.ExternalId] = item; jobSeen.Add(item.ExternalId); }
                        if (!await SaveAsync(job, number, page.CurrentUrl, gathered.Values.ToArray(), false, null,
                            "Неполный сбор: проверка области", snapshot.Map, snapshot.Changes)) return;
                        // A map shell can display cards/reset while its renderer still loads.
                        // Bound this wait by time, not by the number of scroll attempts.
                        int areaWait = snapshot.Loading ? Math.Max(60, settings.LoadWaitSeconds)
                            : area.ProblemCode == "AREA_SELECTION_UNCONFIRMED" ? Math.Max(15, settings.LoadWaitSeconds) : settings.LoadWaitSeconds;
                        if (area.ProblemCode != "AREA_GEOMETRY_CHANGED" && initialReady.Elapsed.TotalSeconds < areaWait)
                        { if (snapshot.Loading) step--; await Task.Delay(250, token).ConfigureAwait(false); continue; }
                        string reason = snapshot.Loading ? "Карта не завершила загрузку за отведённое время; сбор остановлен"
                            : area.ProblemCode == "AREA_GEOMETRY_CHANGED" ? "Полученный контур отличается от сохранённого эталона"
                            : area.ProblemCode == "AREA_SELECTION_UNCONFIRMED" ? "Карта или активное выделение не подтверждены; сбор остановлен"
                            : "Текущий контур области не получен; сбор остановлен. Восстановите выделение на карте";
                        await FinishAsync(job, page, JobState.Failed, CollectionCompletionKind.Partial, snapshot.Loading ? CollectionResultReasonCodes.LoadingInterrupted : CollectionResultReasonCodes.EndNotConfirmed,
                            reason + "; проверенные объявления сохранены, неподтверждённые — в локальной диагностике",
                            snapshot.Warnings, false, !snapshot.Loading, stableRounds); return;
                    }
                }
                if (snapshot.AreaCheck is null && snapshot.Map is { ZoneConfirmed: false } unconfirmed)
                {
                    if (initialReady.Elapsed.TotalSeconds < settings.LoadWaitSeconds)
                    { await Task.Delay(250, token).ConfigureAwait(false); continue; }
                    if (serverManaged)
                    {
                        await FinishAsync(job, page, JobState.Failed, CollectionCompletionKind.SourceError, CollectionResultReasonCodes.InvalidSearchUrl,
                            "Карта: " + (unconfirmed.ZoneError ?? "контур зоны не подтверждён"), snapshot.Warnings, false, false, stableRounds);
                        return;
                    }
                    Block(job, job.Owner!, "Карта: " + (unconfirmed.ZoneError ?? "контур зоны не получен; восстановите выделение"));
                    await ReadyAsync(job.Source, token).ConfigureAwait(false);
                    stable.Restart(); initialReady.Restart(); continue;
                }
                map = snapshot.Map;
                if (SearchUrls.IsDetail(page.CurrentUrl, job.Source))
                { await FinishAsync(job, page, JobState.Failed, CollectionCompletionKind.SourceError, CollectionResultReasonCodes.InvalidSearchUrl,
                    "Ссылка сохранена, но парсер отдельного объявления пока не реализован", [], false, false, stableRounds); return; }
                if (snapshot.Kind == PageKind.Unknown && gathered.Count == 0 && initialReady.Elapsed.TotalSeconds < settings.LoadWaitSeconds)
                { await Task.Delay(250, token).ConfigureAwait(false); continue; }
                if (snapshot.Kind != PageKind.SearchResults)
                {
                    await FinishAsync(job, page, JobState.Failed, CollectionCompletionKind.SourceError,
                        snapshot.Kind == PageKind.SourceError ? CollectionResultReasonCodes.SourceUnavailable : CollectionResultReasonCodes.InvalidSourceResponse,
                        snapshot.Kind + ": страница не является успешной выдачей", snapshot.Warnings, false, false, stableRounds);
                    if (settings.ErrorPolicy == ErrorPolicy.PauseSource)
                    { Block(job, job.Owner!, snapshot.Kind.ToString()); await ReadyAsync(job.Source, token).ConfigureAwait(false); }
                    return;
                }
                // A confirmed polygon does not authorize changed prices/deal/opaque filters.
                // Compare against the requested search, before the site's first redirect.
                bool identityMatches = snapshot.AreaCheck is { FiltersMatch: true, GeometryMatches: true } && (map is not null || SearchUrls.PageNumber(page.CurrentUrl) == number)
                    || (job.Source == SourceSite.Avito && map is not null
                    ? AvitoSearchIdentity.Confirmed(job.Url, page.CurrentUrl, map, snapshot.AvitoNeutralFormConfirmed)
                    : SearchUrls.PageNumber(page.CurrentUrl) == number
                        && (job.Source == SourceSite.Avito && AvitoMapData.DrawId(job.Url) is not null
                            ? snapshot.AvitoListSearchConfirmed && AvitoSearchIdentity.ConfirmsList(job.Url, page.CurrentUrl, page.CurrentUrl, snapshot.AvitoNeutralFormConfirmed)
                            : SearchUrls.SameSearch(effectiveSearch, page.CurrentUrl, job.Source)));
                if (!identityMatches)
                {
                    diagnostics?.Write(job, number, "Проверка адреса", "Несовпадение", expectedUrl: effectiveSearch, actualUrl: page.CurrentUrl,
                        detail: "Номер страницы или фильтры отличаются; источник изменения неизвестен");
                    await FinishAsync(job, page, JobState.Failed, CollectionCompletionKind.SourceError, CollectionResultReasonCodes.InvalidSearchUrl,
                        snapshot.SearchIdentityError ?? "Номер страницы или фильтры изменились; сравнение адресов записано в журнал", [], false, false, stableRounds); return;
                }
                if (map is null) effectiveSearch = page.CurrentUrl;
                await ReadyAsync(job.Source, token).ConfigureAwait(false);
                int before = gathered.Count;
                foreach (ListingObservation item in snapshot.Listings) { gathered[item.ExternalId] = item; jobSeen.Add(item.ExternalId); }
                foreach (string warning in snapshot.Warnings) { pageWarnings.Add(warning); jobWarnings.Add(warning); }
                if (gathered.Count > before) { stableRounds = 0; stable.Restart(); }
                else stableRounds++;
                if (!await SaveAsync(job, number, page.CurrentUrl, gathered.Values.ToArray(), false, null,
                    pageWarnings.Count == 0 ? map is null ? "Частичный снимок" : $"Карта: {gathered.Count} / {map.ExpectedCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"}; порций {map.ResponseBatches}" : string.Join("; ", pageWarnings), map, snapshot.Changes)) return;
                Changed?.Invoke(this, EventArgs.Empty);
                if (snapshot.Loading)
                {
                    loadingSince ??= DateTimeOffset.UtcNow;
                    stableRounds = 0; stable.Restart();
                    if ((DateTimeOffset.UtcNow - loadingSince.Value).TotalSeconds >= loadingTimeoutSeconds)
                    {
                        CollectionCompletionKind kind = gathered.Count > 0 ? CollectionCompletionKind.Partial : CollectionCompletionKind.SourceError;
                        await FinishAsync(job, page, JobState.Failed, kind, CollectionResultReasonCodes.LoadingInterrupted,
                            "Загрузка не завершилась; полезные данные сохранены", jobWarnings, false, false, stableRounds); return;
                    }
                    await Task.Delay(250, token).ConfigureAwait(false);
                    continue;
                }
                loadingSince = null;
                if (map is { ResponseBatches: 0 } && gathered.Count == 0 && initialReady.Elapsed.TotalSeconds < loadingTimeoutSeconds)
                {
                    stableRounds = 0; stable.Restart();
                    await Task.Delay(250, token).ConfigureAwait(false);
                    continue;
                }
                await ReadyAsync(job.Source, token).ConfigureAwait(false);
                bool bottom = await ActionAsync(job, page, number, "Прокрутка", () => page.WheelAsync(settings, token), token).ConfigureAwait(false);
                if (!bottom) stable.Restart();
                if (bottom && !snapshot.Loading && stableRounds >= 3 && stable.Elapsed.TotalSeconds >= settings.StabilitySeconds) { settled = true; break; }
                int pause = map is null ? settings.ScrollPauseMilliseconds : Math.Max(750, settings.ScrollPauseMilliseconds);
                await Task.Delay(pause, token).ConfigureAwait(false);
            }
            if (!settled)
            {
                string code = lastLoading ? CollectionResultReasonCodes.LoadingInterrupted : CollectionResultReasonCodes.EndNotConfirmed;
                CollectionCompletionKind kind = gathered.Count > 0 || areaGuard?.LastCheck?.ProblemCode is not null
                    ? CollectionCompletionKind.Partial : CollectionCompletionKind.SourceError;
                if (lastMapDiagnostic is not null)
                    diagnostics?.Write(job, number, "Диагностика карты", "Итог", detail: lastMapDiagnostic, count: gathered.Count);
                string progress = map?.ExpectedCount is int hint ? $"{gathered.Count} / {hint}" : gathered.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                await FinishAsync(job, page, JobState.Failed, kind, code, $"Прокрутка не подтвердила конец выдачи; сохранено {progress}; полезные данные сохранены",
                    jobWarnings, false, !lastLoading, stableRounds); return;
            }
            await ReadyAsync(job.Source, token).ConfigureAwait(false);
            PageObservation final = await ActionAsync(job, page, number, "Контроль выдачи", () => page.ReadAsync(token), token).ConfigureAwait(false);
            if (areaGuard is null && job.Source == SourceSite.Avito && final.Map is not null)
                areaGuard = new(SearchAreas, job.Url, serverManaged, job.Id);
            if (areaGuard is not null && final.Kind == PageKind.SearchResults && !final.Loading && final.Map is null)
            {
                await FinishAsync(job, page, JobState.Failed, CollectionCompletionKind.SourceError, CollectionResultReasonCodes.LayoutChanged,
                    "Карта сменилась списком при контрольном чтении; проверенные данные сохранены", [], false, true, stableRounds); return;
            }
            if (areaGuard is not null) final = areaGuard.Apply(final, page.CurrentUrl);
            foreach (ListingDataQualityDiagnostic item in final.DataQuality ?? [])
                qualityDiagnostics[item.ExternalId] = item;
            if (final.SourceCountHint is int finalHint)
            {
                if (sourceCountHint is int previous && previous != finalHint) jobWarnings.Add("SOURCE_COUNT_HINT_CHANGED");
                sourceCountHint = finalHint;
            }
            if (final.Diagnostic is not null) diagnostics?.Write(job, number, "Диагностика карты", "Итог", detail: MapDiagnosticSummary(final.Diagnostic), count: final.Listings.Length);
            if (final.Loading)
            {
                CollectionCompletionKind kind = gathered.Count > 0 ? CollectionCompletionKind.Partial : CollectionCompletionKind.SourceError;
                await FinishAsync(job, page, JobState.Failed, kind, CollectionResultReasonCodes.LoadingInterrupted,
                    "Загрузка не завершилась; полезные данные сохранены", jobWarnings, false, false, stableRounds); return;
            }
            if (final.Kind != PageKind.SearchResults)
            {
                if (final.Kind == PageKind.RateLimited && serverManaged)
                { await FinishAsync(job, page, JobState.Failed, CollectionCompletionKind.RateLimited, "", "Источник временно ограничил запросы", final.Warnings, false, false, stableRounds); return; }
                if (final.Kind is PageKind.Captcha or PageKind.AuthenticationRequired or PageKind.RateLimited)
                { Block(job, job.Owner!, final.Kind.ToString()); await ReadyAsync(job.Source, token).ConfigureAwait(false); continue; }
                await FinishAsync(job, page, JobState.Failed, CollectionCompletionKind.SourceError,
                    final.Kind == PageKind.SourceError ? CollectionResultReasonCodes.SourceUnavailable : CollectionResultReasonCodes.InvalidSourceResponse,
                    final.Kind.ToString(), final.Warnings, false, false, stableRounds); return;
            }
            if (final.AreaCheck is { } finalArea && (!finalArea.FiltersMatch || !finalArea.GeometryMatches
                || final.Map is null && SearchUrls.PageNumber(page.CurrentUrl) != number))
            {
                if (finalArea.FiltersMatch)
                {
                    foreach (ListingObservation item in final.Listings) { gathered[item.ExternalId] = item; jobSeen.Add(item.ExternalId); }
                    if (!await SaveAsync(job, number, page.CurrentUrl, gathered.Values.ToArray(), false, null,
                        "Неполный контроль области; проверенные данные сохранены", final.Map, final.Changes)) return;
                }
                await FinishAsync(job, page, JobState.Failed, finalArea.FiltersMatch ? CollectionCompletionKind.Partial : CollectionCompletionKind.SourceError,
                    finalArea.FiltersMatch ? CollectionResultReasonCodes.EndNotConfirmed : CollectionResultReasonCodes.InvalidSearchUrl,
                    "Контроль области не пройден; проверенные данные сохранены, эталон не изменён", final.Warnings, false, true, stableRounds); return;
            }
            if (final.AreaCheck is null && final.Map is { ZoneConfirmed: false })
            {
                if (serverManaged)
                { await FinishAsync(job, page, JobState.Failed, CollectionCompletionKind.SourceError, CollectionResultReasonCodes.InvalidSearchUrl,
                    "Карта: зона сбросилась или изменилась", final.Warnings, false, true, stableRounds); return; }
                Block(job, job.Owner!, "Карта: зона сбросилась или изменилась"); await ReadyAsync(job.Source, token).ConfigureAwait(false); continue;
            }
            map = final.Map;
            if (final.AreaCheck is null && job.Source == SourceSite.Avito && (map is not null || AvitoMapData.DrawId(job.Url) is not null)
                && !(map is not null ? AvitoSearchIdentity.Confirmed(job.Url, page.CurrentUrl, map, final.AvitoNeutralFormConfirmed)
                    : final.AvitoListSearchConfirmed && SearchUrls.PageNumber(page.CurrentUrl) == number
                        && AvitoSearchIdentity.ConfirmsList(job.Url, page.CurrentUrl, page.CurrentUrl, final.AvitoNeutralFormConfirmed)))
            {
                await FinishAsync(job, page, JobState.Failed, CollectionCompletionKind.SourceError, CollectionResultReasonCodes.InvalidSearchUrl,
                    "Контроль выдачи: исходная зона или фильтры изменились", final.Warnings, false, true, stableRounds); return;
            }
            foreach (ListingObservation item in final.Listings) { gathered[item.ExternalId] = item; jobSeen.Add(item.ExternalId); }
            if (!await SaveAsync(job, number, page.CurrentUrl, gathered.Values.ToArray(), false, null, "Контрольный частичный снимок", map, final.Changes)) return;
            foreach (string warning in final.Warnings) { pageWarnings.Add(warning); jobWarnings.Add(warning); }
            if (pageWarnings.Any(warning => !warning.StartsWith("AREA_", StringComparison.Ordinal)) && !serverManaged)
            {
                await FinishAsync(job, page, JobState.Failed, CollectionCompletionKind.SourceError, CollectionResultReasonCodes.InvalidSourceResponse,
                    "Ошибки карточек: " + string.Join("; ", pageWarnings), pageWarnings, false, true, stableRounds); return;
            }
            diagnostics?.WriteDataQuality(job, number, page.CurrentUrl, gathered.Values, qualityDiagnostics);
            Pagination next = await ActionAsync(job, page, number, "Поиск следующей страницы", () => page.NextAsync(token), token).ConfigureAwait(false);
            if (next.Kind == NextKind.UnknownInvalid)
            {
                // The current page passed its final checks. A broken link outside
                // the configured list budget cannot invalidate this bounded result.
                if (map is null && number == job.Limit && areaGuard is null)
                {
                    if (!await SaveAsync(job, number, page.CurrentUrl, gathered.Values.ToArray(), true, next,
                        "Страница проверена; достигнут настроенный предел страниц", null)) return;
                    await FinishAsync(job, page, JobState.LimitReached, CollectionCompletionKind.LimitReached,
                        CollectionResultReasonCodes.PageLimitReached, "Достигнут настроенный предел страниц", jobWarnings, false, true, stableRounds, sourceCountHint); return;
                }
                if (diagnostics is not null)
                    await diagnostics.WriteErrorAsync(job, page, number, "Поиск следующей страницы",
                        next.Reason ?? "Пагинация не распознана", "Pagination", next.Url, gathered.Count, token).ConfigureAwait(false);
                await FinishAsync(job, page, JobState.Failed, CollectionCompletionKind.SourceError,
                    CollectionResultReasonCodes.LayoutChanged, next.Reason ?? "Пагинация не распознана", jobWarnings, false, true, stableRounds); return;
            }
            if (next.Kind == NextKind.Next && (next.Url is null || !visited.Add(next.Url))) { await FinishAsync(job, page, JobState.Failed,
                CollectionCompletionKind.SourceError, CollectionResultReasonCodes.InvalidSourceResponse, "PAGINATION_CYCLE", jobWarnings, false, true, stableRounds); return; }
            if (areaGuard?.LastCheck is { ProblemCode: not null })
            {
                await FinishAsync(job, page, JobState.Failed, CollectionCompletionKind.Partial, CollectionResultReasonCodes.EndNotConfirmed,
                    "Неполный сбор: часть объявлений без подтверждённых координат; проверенные данные сохранены",
                    final.Warnings, false, true, stableRounds, sourceCountHint); return;
            }
            await ReadyAsync(job.Source, token).ConfigureAwait(false);
            if (!await SaveAsync(job, number, page.CurrentUrl, gathered.Values.ToArray(), true, next, next.Reason ?? "Страница завершена", map)) return;
            Changed?.Invoke(this, EventArgs.Empty);
            if (next.Kind == NextKind.End)
            {
                if (sourceCountHint is int hint && hint != jobSeen.Count) jobWarnings.Add(CollectionResultReasonCodes.CountHintMismatch);
                await FinishAsync(job, page, JobState.Completed, CollectionCompletionKind.Success, "",
                    map is not null ? $"Карта: собрано {jobSeen.Count}; подсказка источника {map.ExpectedCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "нет"}; правый список завершён"
                    : sourceCountHint is int countHint ? $"Достигнут конец выдачи; собрано {jobSeen.Count}; подсказка источника {countHint}" : "Достигнут конец выдачи",
                    jobWarnings, true, true, stableRounds, sourceCountHint); return;
            }
            if (number == job.Limit) { await FinishAsync(job, page, JobState.LimitReached, CollectionCompletionKind.LimitReached,
                CollectionResultReasonCodes.PageLimitReached, "Достигнут настроенный предел страниц", jobWarnings, false, true, stableRounds, sourceCountHint); return; }
            await NavigateAsync(job.Source, settings.PageIntervalSeconds, () => ActionAsync(job, page, number + 1, "Переход на следующую страницу",
                async () => { await page.FollowAsync(next, token).ConfigureAwait(false); return true; }, token, next.Url), token).ConfigureAwait(false);
            effectiveSearch = page.CurrentUrl;
            number++;
        }
        }
        finally { areaGuard?.Persist(); }
    }
    private async Task<bool> FinishAsync(CollectionJob job, ISourcePage? page, JobState state, CollectionCompletionKind kind, string code,
        string message, IEnumerable<string> warnings, bool endReached, bool loadingCompleted, int stableRounds, int? sourceCountHint = null)
    {
        if (cancellation?.IsCancellationRequested == true) return false;
        string[] safeWarnings = warnings.Where(IsMachineCode).Distinct(StringComparer.Ordinal).Take(20).ToArray();
        bool finished = store.Finish(job, state, message,
            new(kind, endReached, loadingCompleted, Math.Max(0, stableRounds), code, safeWarnings, sourceCountHint));
        if (finished && state == JobState.Failed)
            diagnostics?.WriteFailure(job, store.Job(job.Id)?.Page ?? job.Page,
                message, string.IsNullOrWhiteSpace(code) ? null : code, store.Journal(job.Id).Sum(x => x.Count));
        if (finished && state == JobState.Failed && diagnostics is not null)
            await diagnostics.LastError.SaveAsync(job, page, message, code).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty); return finished;
    }
    private static bool IsMachineCode(string value) => value.Length is > 0 and <= 64
        && value.All(character => char.IsAsciiLetterUpper(character) || char.IsDigit(character) || character == '_');
    private static string ExceptionReason(Exception exception)
    {
        Exception cause = exception is CollectionActionException { InnerException: not null } action ? action.InnerException! : exception;
        if (cause is TimeoutException || cause is PlaywrightException && cause.Message.Contains("Timeout", StringComparison.OrdinalIgnoreCase))
            return CollectionResultReasonCodes.NetworkTimeout;
        if (cause is ArgumentException) return CollectionResultReasonCodes.InvalidSearchUrl;
        if (cause is IOException or PlaywrightException) return CollectionResultReasonCodes.SourceUnavailable;
        return CollectionResultReasonCodes.InvalidSourceResponse;
    }
    private static string MapDiagnosticSummary(MapDiagnostic value)
    {
        MapBatchDiagnostic? last = value.Batches.LastOrDefault();
        return $"карта: собрано={value.Collected}; источник={value.ExpectedCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"}; " +
            $"порций={value.Batches.Length}; json={value.JsonUnique}; loading={value.Loading}; zone={value.ZoneConfirmed}; " +
            $"последняя порция={(last is null ? "нет" : $"получено {last.Received}, новых {last.NewIds}, повторов {last.Repeated}")}; " +
            $"scroll={value.ScrollTop?.ToString("0", System.Globalization.CultureInfo.InvariantCulture) ?? "?"}/" +
            $"{value.ScrollHeight?.ToString("0", System.Globalization.CultureInfo.InvariantCulture) ?? "?"}";
    }

    private async Task<bool> SaveAsync(CollectionJob job, int number, string url, ListingObservation[] listings,
        bool completed, Pagination? pagination, string reason, MapScope? map = null, ListingObservation[]? changes = null)
    {
        while (true)
        {
            await ReadyAsync(job.Source, cancellation!.Token).ConfigureAwait(false);
            lock (sync)
            {
                if (sources[job.Source].Blocked || userPaused) continue;
                bool saved = store.SavePage(job, number, url, listings, completed, pagination, reason, map, changes, cancellation.Token);
                diagnostics?.Write(job, number, "Сохранение страницы", saved ? completed ? "Завершённая" : "Частичная" : "Владение утрачено",
                    actualUrl: url, detail: reason, count: listings.Length);
                return saved;
            }
        }
    }
    private async Task<T> ActionAsync<T>(CollectionJob job, ISourcePage? page, int number, string action,
        Func<Task<T>> operation, CancellationToken token, string? expectedUrl = null)
    {
        System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();
        diagnostics?.Write(job, number, action, "Начато", expectedUrl: expectedUrl, actualUrl: page?.CurrentUrl);
        try
        {
            T result = await operation().ConfigureAwait(false);
            diagnostics?.Write(job, number, action, "Выполнено", duration: timer.ElapsedMilliseconds,
                expectedUrl: expectedUrl, actualUrl: page?.CurrentUrl);
            return result;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            diagnostics?.Write(job, number, action, "Отменено", duration: timer.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex) when (ex is TimeoutException or Microsoft.Playwright.PlaywrightException or InvalidOperationException or ArgumentException)
        {
            diagnostics?.Write(job, number, action, "Ошибка", duration: timer.ElapsedMilliseconds,
                expectedUrl: expectedUrl, actualUrl: page?.CurrentUrl, errorType: ex.GetType().Name);
            if (diagnostics is not null)
                await diagnostics.WriteErrorAsync(job, page, number, action, ex.GetType().Name,
                    ex.GetType().Name, expectedUrl, store.Journal(job.Id).Sum(x => x.Count), token).ConfigureAwait(false);
            throw new CollectionActionException(action, ex);
        }
    }
    private async Task ReadyAsync(SourceSite source, CancellationToken token)
    {
        while (true)
        {
            Task sourceWait, userWait;
            lock (sync) { sourceWait = sources[source].Ready.Task; userWait = userReady.Task; }
            await Task.WhenAll(sourceWait, userWait).WaitAsync(token).ConfigureAwait(false);
            lock (sync) if (!sources[source].Blocked && !userPaused) return;
        }
    }
    private async Task NavigateAsync(SourceSite source, int seconds, Func<Task> action, CancellationToken token)
    {
        SourceControl control = sources[source];
        await control.Navigation.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await ReadyAsync(source, token).ConfigureAwait(false);
            TimeSpan delay = control.NextNavigation - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero) await Task.Delay(delay, token).ConfigureAwait(false);
            await ReadyAsync(source, token).ConfigureAwait(false);
            await action().ConfigureAwait(false);
            control.NextNavigation = DateTimeOffset.UtcNow.AddSeconds(seconds);
        }
        finally { control.Navigation.Release(); }
    }
    private void Block(CollectionJob job, string worker, string reason)
    {
        lock (sync)
        {
            SourceControl control = sources[job.Source];
            if (!control.Blocked) { control.Blocked = true; control.Ready = Signal(false); }
            if (reason is "Captcha" or "AuthenticationRequired" or "RateLimited" || reason.StartsWith("Карта:", StringComparison.Ordinal)) control.VerificationJobs.Add(job.Id);
            foreach (ActiveWorker item in active.Values.Where(x => x.Job.Source == job.Source)) store.SetState(item.Job, JobState.AwaitingManualAction, reason);
        }
        diagnostics?.Write(job, job.Page, "Приостановка источника", reason, detail: "Требуется ручное действие");
        ManualActionRequired?.Invoke(this, new(job.Source, job.Id, job.Url, worker, reason + ": пройдите проверку вручную; затем нажмите «Продолжить источник»"));
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public async Task ActivateManualPageAsync(string jobId, CancellationToken token = default)
    {
        ActiveWorker? item;
        lock (sync) item = active.Values.FirstOrDefault(worker => worker.Job.Id == jobId);
        if (item != null) await item.Page.ActivateAsync(token).ConfigureAwait(false);
    }
    public async Task ResumeSourceAsync(SourceSite source, CancellationToken token = default)
    {
        ActiveWorker[] items; HashSet<string> verification;
        lock (sync) { items = active.Values.Where(x => x.Job.Source == source).ToArray(); verification = new(sources[source].VerificationJobs, StringComparer.Ordinal); }
        foreach (ActiveWorker item in items.Where(x => verification.Contains(x.Job.Id)))
        {
            await item.Page.ActivateAsync(token).ConfigureAwait(false);
            PageObservation snapshot = await item.Page.ReadAsync(token).ConfigureAwait(false);
            // Resume clears a manual protection gate. The collection loop independently
            // validates saved geometry and filters; a changed draw ID is not a CAPTCHA.
            bool usesAreaGuard = source == SourceSite.Avito && (snapshot.Map is not null || AvitoMapData.DrawId(item.Job.Url) is not null);
            if (snapshot.Kind != PageKind.SearchResults || !usesAreaGuard && snapshot.Map is { ZoneConfirmed: false })
                throw new InvalidOperationException($"{source}: {snapshot.Kind}. Проверка ещё не пройдена.");
        }
        lock (sync)
        {
            if (!sources[source].VerificationJobs.SetEquals(verification)) throw new InvalidOperationException("Появилась проверка в другой вкладке. Пройдите её и повторите продолжение.");
            foreach (ActiveWorker item in items) store.SetState(item.Job, userPaused ? JobState.PausedByUser : JobState.Running, "Продолжено вручную");
            sources[source].Blocked = false; sources[source].Ready.TrySetResult();
            sources[source].VerificationJobs.Clear();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void Pause()
    {
        lock (sync)
        {
            if (userPaused) return;
            userPaused = true; userReady = Signal(false);
            foreach (ActiveWorker item in active.Values) if (!sources[item.Job.Source].Blocked) store.SetState(item.Job, JobState.PausedByUser, "Пауза пользователя");
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void Resume()
    {
        lock (sync)
        {
            foreach (ActiveWorker item in active.Values) if (!sources[item.Job.Source].Blocked) store.SetState(item.Job, JobState.Running, "Продолжено пользователем");
            userPaused = false; userReady.TrySetResult();
        }
    }
    // Stop never waits for SQLite on the window thread. RunAsync fences unfinished jobs
    // after cancelled workers finish; ReadyAsync checks cancellation before every save.
    public void Stop()
    {
        Interlocked.Increment(ref stopVersion);
        try { Volatile.Read(ref cancellation)?.Cancel(); }
        catch (ObjectDisposedException) { /* A new launch checks the incremented stop version. */ }
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        try { Stop(); if (Completion is not null) await Completion.ConfigureAwait(false); }
        finally
        {
            try { await sessions.DisposeAsync().ConfigureAwait(false); }
            finally { cancellation?.Dispose(); foreach (SourceControl source in sources.Values) source.Dispose(); }
        }
    }
}
