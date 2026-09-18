using System.ComponentModel.DataAnnotations;
using System.Globalization;
using LandErp.Application.Foundation;
using LandErp.Application.Foundation.Files;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.Collection.Contracts;
using LandErp.Application.Modules.Collection.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Collector.Contracts.V1;

namespace LandErp.Server.Components.Pages;

public partial class Collectors : IAsyncDisposable
{
    private const int PageSize = 10;
    private CollectionAdminView? view;
    private AgentConnectionCode? connectionCode;
    private AgentCredential? credential;
    private AgentView? selectedAgent;
    private CollectionJobView? selectedJob;
    private SearchView? editingSearch;
    private AgentInput agentInput = new();
    private CollectionSearchForm searchInput = new();
    private GroupInput groupInput = new();
    private bool showSearchModal, showConnectionModal, showGroupsModal, showQuickGroup, showAttentionDetails;
    private string quickGroupName = "", query = "", groupFilter = "all", statusFilter = "all";
    private string historyQuery = "", historyFilter = "all", groupVisibility = "active";
    private string? refreshMessage;
    private string previewText = "";
    private bool databaseReady;
    private FileStorageHealth storageHealth = new(false, "NOT_CHECKED");
    private DateTimeOffset? storageCheckedAt;
    private int previewVersion;
    private DateTimeOffset? refreshedAt;
    private int historyPage = 1;
    private readonly HashSet<string> collapsed = [];
    private readonly CancellationTokenSource lifetime = new();
    private Task? refreshTask;

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        refreshTask = RefreshLoopAsync();
    }

    protected override async Task ReadAsync()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        databaseReady = await DatabaseStatus.IsReadyAsync(lifetime.Token);
        if (storageCheckedAt == null || now - storageCheckedAt >= TimeSpan.FromMinutes(1))
        {
            storageHealth = await StorageStatus.CheckAsync(lifetime.Token);
            storageCheckedAt = now;
        }
        refreshedAt = now;
        refreshMessage = null;
        if (!databaseReady) { view = null; return; }
        view = await Administration.ReadAsync(CurrentSubject, lifetime.Token);
    }

    private async Task RefreshLoopAsync()
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(15));
        try
        {
            while (await timer.WaitForNextTickAsync(lifetime.Token))
                await InvokeAsync(async () =>
                {
                    // Never replace form fields or their expected revisions during refresh.
                    if (Busy || Loading || Forbidden) return;
                    try { await ReadAsync(); }
                    catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
                    catch (AccessDeniedException) { view = null; refreshMessage = "Доступ изменился. Обновите страницу."; }
                    catch { refreshMessage = "Не удалось обновить состояние. Показаны последние полученные данные."; }
                    StateHasChanged();
                });
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync();
        if (refreshTask is not null) await refreshTask;
        lifetime.Dispose();
        GC.SuppressFinalize(this);
        credential = null;
        connectionCode = null;
    }

    private bool SchedulerHealthy => view?.Scheduler.State == "Работает";
    private bool WorkerHealthy => view?.Scheduler.LastStartedAt is { } started && refreshedAt is { } refreshed
        && started >= refreshed.AddMinutes(-2);
    private string WorkerState => !databaseReady ? "Нет данных" : view?.Scheduler.LastStartedAt == null ? "Не запускался" : WorkerHealthy ? "Работает" : "Нет связи";
    private int ExpiredLeaseCount => view?.ExpiredLeases ?? 0;
    private int BacklogCount => (view?.PendingJobs ?? 0) + ExpiredLeaseCount;
    private int StaleAgents => view?.Agents.Count(IsAgentStale) ?? 0;
    private int OfflineAgents => view?.Agents.Count(agent => agent.Enabled && !agent.Online && !IsAgentStale(agent)) ?? 0;
    private bool StorageHealthy => storageHealth.Available;
    private int AttentionCount => (view?.AttentionJobs ?? 0) + (view?.Agents.Count(AgentNeedsAttention) ?? 0)
        + (SchedulerHealthy ? 0 : 1) + ExpiredLeaseCount;
    private string AttentionSummary => !SchedulerHealthy
        ? "Автоматические запуски не подтверждены: проверьте службу расписаний. Ручной запуск доступен."
        : "Некоторые поиски или парсеры требуют действия. Откройте причины.";
    private bool HasFilters => query.Length > 0 || groupFilter != "all" || statusFilter != "all" || groupVisibility != "active";
    private string ZoneLabel => view?.BusinessTimeZone ?? "Europe/Moscow";
    private bool HasOpenDialog => showSearchModal || showConnectionModal || showGroupsModal || selectedAgent is not null || selectedJob is not null;

    private IReadOnlyList<GroupSection> FilteredSections
    {
        get
        {
            if (view is null) return [];
            var filtered = view.Searches.Where(Matches).ToArray();
            var result = new List<GroupSection>();
            foreach (var group in view.Groups.Where(g => groupVisibility == "all" || (groupVisibility == "archived" ? !g.Active : g.Active)).OrderBy(g => g.SortOrder))
            {
                var searches = filtered.Where(s => s.GroupId == group.Id).ToArray();
                if (searches.Length > 0 || query.Length == 0 && statusFilter == "all" && (groupFilter == "all" || groupFilter == group.Id.ToString()))
                    result.Add(Build(group.Id.ToString(), group.Id, group.Name, group.Active, searches));
            }
            var ungrouped = filtered.Where(s => s.GroupId is null).ToArray();
            if (ungrouped.Length > 0 && groupVisibility != "archived") result.Add(Build("none", null, "Без группы", true, ungrouped));
            return result;
        }
    }

    private List<CollectionJobView> FilteredJobs => view?.Jobs.Where(j =>
        (historyQuery.Length == 0 || j.Label.Contains(historyQuery, StringComparison.OrdinalIgnoreCase) ||
            (j.Agent?.Contains(historyQuery, StringComparison.OrdinalIgnoreCase) ?? false)) &&
        (historyFilter == "all" || historyFilter == "completed" && j.State is "Completed" or "LimitReached" ||
            historyFilter == "running" && j.State is "Pending" or "Leased" ||
            historyFilter == "attention" && j.AttentionRequired)).ToList() ?? [];
    private int HistoryPages => Math.Max(1, (int)Math.Ceiling(FilteredJobs.Count / (double)PageSize));
    private IEnumerable<CollectionJobView> PagedJobs => FilteredJobs.Skip((Math.Min(historyPage, HistoryPages) - 1) * PageSize).Take(PageSize);
    private bool Matches(SearchView s) =>
        (query.Length == 0 || s.Label.Contains(query, StringComparison.OrdinalIgnoreCase) || s.Url.Contains(query, StringComparison.OrdinalIgnoreCase)) &&
        (groupFilter == "all" || groupFilter == "none" && s.GroupId is null || Guid.TryParse(groupFilter, out var id) && s.GroupId == id) &&
        (statusFilter == "all" || statusFilter == "active" && s.Enabled || statusFilter == "paused" && !s.Enabled || statusFilter == "attention" && NeedsAttention(s));

    private static GroupSection Build(string key, Guid? id, string name, bool active, SearchView[] searches)
    {
        var runs = searches.Where(s => s.LastRun is not null).Select(s => s.LastRun!).ToArray();
        var schedules = searches.Select(s => s.Schedule).Distinct().ToArray();
        return new(key, id, name, active, searches, searches.Count(s => s.Enabled),
            schedules.Length == 0 ? "—" : schedules.Length == 1 ? schedules[0] : "Разные расписания",
            runs.Length == 0 ? null : runs.Max(r => r.CompletedAt ?? r.CreatedAt),
            searches.Where(s => s.Enabled).Select(s => s.NextRunAt).DefaultIfEmpty().Min(),
            runs.Sum(r => r.ProcessedCount), runs.Sum(r => r.NewListingsCount), runs.Sum(r => r.ChangedListingsCount));
    }

    private async Task OpenSearch(Guid? id = null)
    {
        editingSearch = null; searchInput = new() { GroupId = id }; showQuickGroup = false; showSearchModal = true;
        await UpdatePreviewAsync();
    }
    private async Task OpenEditSearch(SearchView search)
    {
        editingSearch = search; searchInput = CollectionSearchForm.From(search); showQuickGroup = false; showSearchModal = true;
        await UpdatePreviewAsync();
    }
    private async Task UpdatePreviewAsync()
    {
        int version = ++previewVersion;
        try
        {
            var next = await Administration.PreviewScheduleAsync(CurrentSubject, searchInput.Schedule(), editingSearch?.Id,
                searchInput.Enabled, lifetime.Token);
            if (version == previewVersion)
                previewText = !searchInput.Enabled ? "На паузе: новые задания по расписанию не создаются." :
                    next.HasValue ? $"Ближайший плановый запуск: {Time(next)} · {ZoneLabel}" : "Автоматических запусков нет.";
        }
        catch (ArgumentException exception) { if (version == previewVersion) previewText = exception.Message; }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch { if (version == previewVersion) previewText = "Не удалось проверить ближайший запуск. Повторите после восстановления связи."; }
    }
    private void CloseSearch() { if (Busy) return; showSearchModal = false; editingSearch = null; }
    private void OpenConnection() { agentInput = new(); connectionCode = null; showConnectionModal = true; }
    private void CloseConnection() { if (Busy) return; showConnectionModal = false; connectionCode = null; }
    private void OpenGroups() { groupInput = new(); showGroupsModal = true; }
    private void CloseGroups() { if (!Busy) showGroupsModal = false; }
    private void OpenAgent(AgentView agent) { credential = null; selectedAgent = agent; }
    private void CloseAgent() { if (Busy) return; credential = null; selectedAgent = null; }
    private void ResetFilters() { query = ""; groupFilter = "all"; statusFilter = "all"; groupVisibility = "active"; }
    private void ShowAttention() => showAttentionDetails = !showAttentionDetails;
    private void ToggleGroup(string key) { if (!collapsed.Add(key)) collapsed.Remove(key); }
    private bool IsExpanded(string key) => !collapsed.Contains(key);
    private async Task AddTime()
    {
        if (searchInput.Times.Count < 12) searchInput.Times.Add(new(""));
        await UpdatePreviewAsync();
    }

    private Task CreateAgentAsync() => ExecuteAsync(async () =>
        connectionCode = await Administration.CreateConnectionCodeAsync(CurrentSubject, agentInput.Name, Correlation(), lifetime.Token));
    private async Task SaveSearchAsync()
    {
        bool saved = false;
        await ExecuteAsync(async () =>
        {
            var schedule = searchInput.Schedule();
            if (editingSearch is null)
                await Administration.CreateSearchAsync(CurrentSubject, new(searchInput.Label, searchInput.Source,
                    searchInput.Url, searchInput.MaxPages, searchInput.GroupId, schedule, searchInput.RunImmediately), Correlation(), lifetime.Token);
            else
                await Administration.UpdateSearchAsync(CurrentSubject, new(editingSearch.Id, editingSearch.Revision,
                    searchInput.Label, searchInput.Source, searchInput.Url, searchInput.MaxPages, searchInput.GroupId,
                    schedule, searchInput.Enabled), Correlation(), lifetime.Token);
            saved = true;
        });
        // Do not invite a duplicate create if the write succeeded but a subsequent read failed.
        if (saved) { CloseSearch(); Success = searchInput.RunImmediately ? "Поиск создан, первый сбор добавлен в очередь." : "Поиск сохранён."; }
    }
    private async Task QuickCreateGroupAsync()
    {
        await ExecuteAsync(async () =>
        {
            searchInput.GroupId = await Administration.CreateGroupAsync(CurrentSubject, quickGroupName, 0, Correlation(), lifetime.Token);
            showQuickGroup = false; quickGroupName = "";
        });
    }
    private Task CreateGroupAsync() => ExecuteAsync(async () =>
    {
        await Administration.CreateGroupAsync(CurrentSubject, groupInput.Name, groupInput.SortOrder, Correlation(), lifetime.Token);
        groupInput = new();
    });
    private Task ArchiveGroupAsync(SearchGroupView group) => ExecuteAsync(() =>
        Administration.ArchiveGroupAsync(CurrentSubject, group.Id, group.Revision, Correlation(), lifetime.Token));
    private async Task EnqueueAsync(Guid id)
    {
        await ExecuteAsync(() => Administration.EnqueueAsync(CurrentSubject, id, Correlation(), lifetime.Token));
        if (Error is null && !Forbidden) Success = "Сбор добавлен в очередь. Его возьмёт свободный совместимый парсер.";
    }
    private async Task ToggleAgentSearchManagementAsync(AgentView agent)
    {
        await ExecuteAsync(() => Administration.SetAgentSearchManagementAsync(CurrentSubject, agent.Id, agent.Revision, !agent.CanManageSearches, Correlation(), lifetime.Token));
        selectedAgent = view?.Agents.FirstOrDefault(a => a.Id == agent.Id);
    }
    private async Task RotateAsync(AgentView agent)
    {
        await ExecuteAsync(async () => credential = await Administration.RotateCredentialAsync(CurrentSubject, agent.Id, agent.Revision, Correlation(), lifetime.Token));
        selectedAgent = view?.Agents.FirstOrDefault(a => a.Id == agent.Id);
    }
    private async Task RevokeAsync(AgentView agent)
    {
        await ExecuteAsync(() => Administration.RevokeAgentAsync(CurrentSubject, agent.Id, agent.Revision, Correlation(), lifetime.Token));
        selectedAgent = view?.Agents.FirstOrDefault(a => a.Id == agent.Id);
    }
    private static string Correlation() => Guid.CreateVersion7().ToString();
    private string ConnectionCode(AgentConnectionCode code)
    {
        var payload = $"{Navigation.BaseUri}|{code.AgentId:N}|{code.ActivationSecret}";
        return "LDP1." + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payload)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
    private static bool NeedsAttention(SearchView search) => search.LastRun?.AttentionRequired == true;
    private bool IsAgentStale(AgentView agent) => agent.Enabled && !agent.Online && agent.LastHeartbeatAt is { } heartbeat
        && refreshedAt is { } refreshed && heartbeat >= refreshed.AddMinutes(-10);
    private static bool AgentNeedsAttention(AgentView agent) => agent.Enabled && (agent.RuntimeState == AgentRuntimeState.AwaitingManualAction || !string.IsNullOrWhiteSpace(agent.AttentionCode));
    private string Time(DateTimeOffset? instant) => instant.HasValue
        ? TimeZoneInfo.ConvertTime(instant.Value, TimeZoneInfo.FindSystemTimeZoneById(ZoneLabel)).ToString("dd.MM HH:mm", CultureInfo.InvariantCulture) : "—";
    private static string LastRunTitle(CollectionRunView? run) => run is null ? "Ещё не запускался" : JobLabel(run.State);
    private static string JobLabel(string state) => state switch
    {
        "Pending" => "В очереди", "Leased" => "Выполняется", "Completed" => "Завершён",
        "LimitReached" => "Завершён по лимиту", "Partial" => "Собрано частично", "RateLimited" => "Отложен источником",
        "AwaitingManualAction" => "Нужно действие", "Interrupted" => "Прерван", _ => "Ошибка"
    };
    private static string JobTone(string state) => state switch
    {
        "Completed" => "success", "LimitReached" => "neutral", "Pending" or "Leased" => "info",
        "Partial" or "RateLimited" or "AwaitingManualAction" => "warning", _ => "danger"
    };
    private static string ReasonLabel(string code) => code switch
    {
        CollectionResultReasonCodes.CountHintMismatch => "Количество на странице отличается от подсказки источника",
        CollectionResultReasonCodes.EndNotConfirmed => "Конец выдачи не подтверждён",
        CollectionResultReasonCodes.LoadingInterrupted => "Загрузка выдачи прервалась",
        CollectionResultReasonCodes.NetworkTimeout => "Источник не ответил вовремя",
        CollectionResultReasonCodes.SourceUnavailable => "Источник временно недоступен",
        CollectionResultReasonCodes.LayoutChanged => "Структура страницы изменилась",
        CollectionResultReasonCodes.InvalidSearchUrl => "Ссылка поиска некорректна",
        CollectionResultReasonCodes.InvalidSourceResponse => "Ответ источника не распознан",
        CollectionResultReasonCodes.PageLimitReached => "Достигнут настроенный предел страниц",
        CollectionResultReasonCodes.AgentInterrupted => "Работа Parser была прервана",
        CollectionResultReasonCodes.LeaseExpiredOrReplaced => "Назначение Parser устарело или заменено",
        _ => code
    };
    private static string CoverageText(CollectionCoverage coverage) => coverage.SourceCountHint.HasValue
        ? $"Собрано {coverage.UniqueObserved}; источник сообщил {coverage.SourceCountHint}; конец {(coverage.EndReached ? "подтверждён" : "не подтверждён")}."
        : $"Собрано {coverage.UniqueObserved}; конец {(coverage.EndReached ? "подтверждён" : "не подтверждён")}.";
    private static string AgentBadgeTone(AgentView agent) => AgentNeedsAttention(agent) ? "warning" : agent.Online ? "success" : "neutral";
    private static string HealthTone(bool healthy) => healthy ? "ok" : "bad";
    private static string StorageLabel(FileStorageHealth health) => health.Available ? "Работает" : health.Code switch
    {
        "STORAGE_AUTH" => "Ошибка доступа",
        "STORAGE_TIMEOUT" => "Таймаут",
        "STORAGE_UNAVAILABLE" or "STORAGE_DISCONNECTED" or "STORAGE_LOCAL_UNAVAILABLE" => "Недоступно",
        "STORAGE_PATH_CONFLICT" => "Ошибка пути",
        _ => "Требует проверки"
    };
    private static string AgentTone(AgentView agent) => AgentNeedsAttention(agent) ? "attention" : agent.Online ? "online" : "offline";
    private static string Capabilities(string value) => string.IsNullOrWhiteSpace(value) ? "источники не зарегистрированы" : value.Replace(",", ", ");
    private static int Progress(AgentView a) => a.ProgressTotal is > 0 ? Math.Clamp((int)Math.Round(a.ProgressProcessed * 100d / a.ProgressTotal.Value), 0, 100)
        : a.ProgressMaxPages is > 0 && a.ProgressCurrentPage.HasValue ? Math.Clamp((int)Math.Round(a.ProgressCurrentPage.Value * 100d / a.ProgressMaxPages.Value), 0, 100) : 0;
    private static string ProgressLabel(AgentView a) => a.ProgressTotal is > 0 ? $"{a.ProgressProcessed} из {a.ProgressTotal}"
        : a.ProgressCurrentPage.HasValue ? $"страница {a.ProgressCurrentPage} из {a.ProgressMaxPages}" : $"обработано {a.ProgressProcessed}";
    private static string SourceShort(CatalogSource source) => source switch { CatalogSource.Avito => "A", CatalogSource.Cian => "Ц", _ => source.ToString()[..1] };
    private sealed record GroupSection(string Key, Guid? GroupId, string Name, bool Active, IReadOnlyList<SearchView> Searches,
        int ActiveCount, string Schedule, DateTimeOffset? LastRunAt, DateTimeOffset? NextRunAt, int Processed, int NewListings, int Changed);
    private sealed class AgentInput { [Required, MaxLength(200)] public string Name { get; set; } = ""; }
    private sealed class GroupInput { [Required, MaxLength(200)] public string Name { get; set; } = ""; [Range(0, 10000)] public int SortOrder { get; set; } }
}
