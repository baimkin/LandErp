using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using LandErp.ParserSpike.LocalCollection;
using LandErp.ParserSpike.ServerIntegration;
using LandErp.Collector.Contracts.V1;
using Microsoft.Win32;

namespace LandErp.ParserSpike.Desktop;

public sealed record WorkspaceSearch(string Id, string Label, string Url, string Source, string? GroupId,
    LocalSchedule Schedule, bool Enabled, LocalScheduledLink? Local = null, CollectorSearchView? Remote = null)
{
    public string State => Enabled ? "Включён" : "Приостановлен";
    public string ScheduleText => Schedule.Kind switch { LocalScheduleKind.Interval => $"Каждые {Schedule.IntervalMinutes} мин",
        LocalScheduleKind.FixedTimes => string.Join(", ", Schedule.FixedTimes ?? []), _ => "Вручную" };
}
public sealed record WorkspaceFilterOption(string? Id, string Label);
public sealed record WorkspaceJobRow(string Id, string Label, string Source, string State, string Progress, string Started, string Reason);

/// <summary>One visible workspace, one destination. Local data is never uploaded by switching modes.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Window Closing awaits operations; dispatcher-owned synchronization resources live until the window closes.")]
public partial class WorkspaceWindow : Window
{
    private WorkspaceController? controller;
    private readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim operations = new(1, 1);
    private WorkspaceSearch[] searches = [];
    private CollectorWorkspace? remote;
    private bool ready, closing, changingMode, ticking, modalOpen;
    private DateTimeOffset workspaceReadAfter;
    private int offset;
    private long total;
    private int historyOffset;
    private long historyTotal, jobChanges, displayedJobChanges = -1;
    private CollectionJob[] filterJobs = [];
    private SearchLink[] resultLinks = [];
    private long detailsRequest;
    private string? displayedRunError;
    private SourceSite? noticeSource;
    private readonly ManualAttentionState attention = new();
    private WindowsAttentionNotification? attentionNotification;
    private ListingDetailsWindow? detailsWindow;
    private bool IsServer => controller?.Mode == ParserOperatingMode.Server;
    public WorkspaceWindow() : this(null) { }
    public WorkspaceWindow(WorkspaceController? controller)
    {
        InitializeComponent(); this.controller = controller;
        DetailsPanel.OpenListingRequested += OpenListingRequested;
        InitializeResultFilters();
        Loaded += async (_, _) =>
        {
            try
            {
                string root = WorkspaceController.WorkspaceRoot();
                this.controller ??= await Task.Run(() => new WorkspaceController(Path.Combine(root, "local-data", "spike-002.sqlite"), Path.Combine(root, "browser-profiles"),
                    diagnosticsOnly: Environment.GetCommandLineArgs().Contains("--map-diagnostics", StringComparer.Ordinal)), lifetime.Token);
                DiagnosticsPathText.Text = this.controller.Diagnostics.RootPath;
                this.controller.Runner.ManualActionRequired += OnNotice;
                this.controller.Runner.Changed += JobsChanged;
                CollectionSettings settings = this.controller.Store.Settings();
                BrowserInput.SelectedIndex = settings.Browser == "msedge" ? 1 : 0;
                MaxPagesInput.Text = settings.MaxPages.ToString(CultureInfo.CurrentCulture); FreshnessInput.Text = settings.FreshnessHours.ToString(CultureInfo.CurrentCulture);
                ListingDetailsModeInput.SelectedIndex = this.controller.ListingDetailsMode == ListingDetailsDisplayMode.SeparateWindow ? 1 : 0;
                ModeInput.SelectedIndex = IsServer ? 1 : 0; ready = true;
                ApplyListingDetailsMode();
                ShowStatus(); await ActionAsync(ReloadAsync); refresh.Start();
            }
            catch (Exception ex) { StatusText.Text = FriendlyError(ex); }
        };
        refresh.Tick += async (_, _) =>
        {
            if (ready && !closing) RefreshAttention();
            if (!ready || closing || ticking) return;
            bool ownsOperation = await operations.WaitAsync(0);
            if (!ownsOperation && !modalOpen) return;
            ticking = true;
            try
            {
                await Task.Run(() => this.controller!.TickAsync(lifetime.Token), lifetime.Token);
                if (ownsOperation)
                {
                    if (IsServer && remote == null && this.controller!.Server != null && DateTimeOffset.UtcNow >= workspaceReadAfter)
                    { workspaceReadAfter = DateTimeOffset.UtcNow.AddSeconds(30); await LoadSearchesAsync(); }
                    await RefreshJobsAsync(); ShowStatus();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { StatusText.Text = FriendlyError(ex); }
            finally { ticking = false; if (ownsOperation) operations.Release(); }
        };
        Closing += async (_, e) =>
        {
            if (closing) return;
            e.Cancel = true; closing = true; refresh.Stop(); lifetime.Cancel();
            attentionNotification?.Dispose();
            await operations.WaitAsync();
            while (ticking) await Task.Delay(20);
            try
            {
                if (detailsWindow != null) { detailsWindow.Close(); detailsWindow = null; }
                if (this.controller != null)
                {
                    this.controller.Runner.ManualActionRequired -= OnNotice;
                    this.controller.Runner.Changed -= JobsChanged;
                    await Task.Run(async () => await this.controller.DisposeAsync());
                }
            }
            catch (Exception ex) { StatusText.Text = FriendlyError(ex); }
            finally { operations.Release(); _ = Dispatcher.InvokeAsync(Close); }
        };
    }
    public static string FriendlyError(Exception ex) => ex switch
    {
        ServerDeliveryException server => server.Code switch
        {
            CollectorErrorCodes.SearchPermissionRequired => "В LandErp нужно разрешить этому парсеру управление поисками.",
            CollectorErrorCodes.AgentUnauthorized => "Подключение отозвано. Вставьте новый код из LandErp в настройках.",
            "ACTIVATION_USED" or "ACTIVATION_EXPIRED" => "Код уже использован или устарел. Создайте новый код подключения в LandErp.",
            "ACTIVATION_INVALID" => "Сервер не принял код. Скопируйте полный код из LandErp.",
            "SEARCH_BUSY" => "Поиск уже в очереди или выполняется. Дождитесь завершения и обновите список.",
            "SEARCH_CHANGED" or "GROUP_CHANGED" or "CONFLICT" or "CONCURRENCY_CONFLICT" => "Данные уже изменились на сервере. Обновите список и повторите.",
            "GROUP_HAS_ACTIVE_SEARCHES" => "Сначала перенесите или приостановите поиски этой группы.",
            "SEARCH_DISABLED" => "Сначала включите этот поиск.",
            _ => "Сервер не выполнил действие. Проверьте соединение и повторите. Код: " + server.Code
        },
        Microsoft.Data.Sqlite.SqliteException => "Не удалось сохранить данные. Проверьте, нет ли группы или ссылки с таким же названием/адресом.",
        ArgumentException => "Проверьте введённые данные. " + ex.Message,
        InvalidOperationException => ex.Message,
        OperationCanceledException => "Действие отменено.",
        _ => "Действие не выполнено. Проверьте соединение, браузер и доступ к папке данных."
    };
    private async Task ActionAsync(Func<Task> action)
    {
        if (controller == null || closing) return;
        await operations.WaitAsync();
        try { if (!closing) await action(); }
        catch (Exception ex) { StatusText.Text = FriendlyError(ex); }
        finally { operations.Release(); ShowStatus(); }
    }
    private T DuringDialog<T>(Func<T> action)
    {
        modalOpen = true; controller!.SuspendNewWork = true;
        try { return action(); }
        finally { modalOpen = false; controller.SuspendNewWork = false; }
    }
    private void ShowStatus()
    {
        if (controller == null) return;
        AutomationButton.Content = controller.AutomationEnabled ? "Выключить автоработу" : "Включить автоработу";
        ModeDescription.Text = IsServer ? "Поиски и расписания вашей организации" : "Самостоятельная работа на этом компьютере";
        ConnectionText.Text = IsServer ? controller.ConnectionStatus : "Сервер не требуется";
        ServerAddressText.Text = controller.SavedServerConnection()?.Origin.ToString() ?? "Сервер пока не подключён";
        TransferButton.Visibility = ArchiveButton.Visibility = IsServer ? Visibility.Collapsed : Visibility.Visible;
        ResetServerSyncButton.Visibility = IsServer ? Visibility.Visible : Visibility.Collapsed;
        ResetServerSyncButton.IsEnabled = controller.SavedServerConnection() != null;
        StartButton.IsEnabled = !controller.Runner.IsRunning;
        MapDiagnosticUrlInput.Visibility = MapDiagnosticOpenButton.Visibility = MapDiagnosticSaveButton.Visibility = controller.DiagnosticsOnly ? Visibility.Visible : Visibility.Collapsed;
        if (controller.DiagnosticsOnly)
        {
            Title = "LandErp Parser — диагностика карты (без сбора)";
            ModeDescription.Text = "Диагностика карты. Объявления не собираются и не отправляются.";
            ConnectionText.Text = "Авторабота отключена в этом запуске";
            AutomationButton.IsEnabled = StartButton.IsEnabled = SaveSearchAreaButton.IsEnabled = false;
        }
        if (controller.Runner.LastError is { } error && displayedRunError != error)
        { displayedRunError = error; StatusText.Text = error; }
        if (controller.Runner.LastError is null) displayedRunError = null;
    }
    private async Task ReloadAsync() { await LoadSearchesAsync(); await RefreshJobsAsync(true); offset = 0; await FindAsync(); }
    private async Task LoadSearchesAsync()
    {
        string? selectedSearch = (LinksGrid.SelectedItem as WorkspaceSearch)?.Id;
        string? groupId = (GroupsList.SelectedItem as WorkspaceGroup)?.Id;
        WorkspaceGroup[] groups;
        if (IsServer)
        {
            if (controller!.DiagnosticsOnly) remote = await controller.ReadDiagnosticWorkspaceAsync(lifetime.Token);
            else { await controller.ConnectServerAsync(); remote = await controller.Server!.ReadWorkspaceAsync(lifetime.Token); }
            groups = remote.Groups.Where(x => x.Active).Select(x => new WorkspaceGroup(x.Id.ToString(), x.Name)).ToArray();
            searches = remote.Searches.Select(x => new WorkspaceSearch(x.Id.ToString(), x.Label, x.Url, x.Source.ToString(), x.GroupId?.ToString(),
                new((LocalScheduleKind)x.Schedule.Kind, x.Schedule.IntervalMinutes, x.Schedule.FixedTimes), x.Enabled, Remote: x)).ToArray();
        }
        else
        {
            var local = await Task.Run(() => (Groups: controller!.Store.Groups(), Searches: controller.Store.ScheduledLinks()), lifetime.Token);
            groups = local.Groups.Where(x => x.Active).Select(x => new WorkspaceGroup(x.Id, x.Name)).ToArray();
            searches = local.Searches.Select(x => new WorkspaceSearch(x.Link.Id, x.Link.Label, x.Link.Url, x.Link.Source.ToString(), x.GroupId, x.Schedule, x.Link.Enabled, x)).ToArray();
        }
        WorkspaceGroup[] all = [new(null, "Все поиски"), new("", "Без группы"), .. groups];
        GroupsList.ItemsSource = all; GroupsList.SelectedItem = all.FirstOrDefault(x => x.Id == groupId) ?? all[0]; ApplyGroup();
        LinksGrid.SelectedItem = LinksGrid.Items.Cast<WorkspaceSearch>().FirstOrDefault(x => x.Id == selectedSearch);
        RefreshResultFilters();
    }
    private void ApplyGroup()
    {
        if (LinksGrid == null) return;
        string? id = (LinksGrid.SelectedItem as WorkspaceSearch)?.Id;
        string? group = (GroupsList.SelectedItem as WorkspaceGroup)?.Id;
        WorkspaceSearch[] rows = searches.Where(x => group == null || (group == "" ? x.GroupId == null : x.GroupId == group)).ToArray();
        LinksGrid.ItemsSource = rows; LinksGrid.SelectedItem = rows.FirstOrDefault(x => x.Id == id);
        EmptyText.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void GroupSelected(object sender, SelectionChangedEventArgs e) => ApplyGroup();
    private void JobsChanged(object? sender, EventArgs e) => Interlocked.Increment(ref jobChanges);
    private async Task RefreshJobsAsync(bool force = false)
    {
        if (controller == null) return;
        bool serverWork = IsServer;
        long revision = Volatile.Read(ref jobChanges);
        bool historyChanged = force || revision != displayedJobChanges;
        var snapshot = await Task.Run(() => (Active: controller.Store.ReadJobPage(serverWork, activeOnly: true),
            History: historyChanged ? controller.Store.ReadJobPage(serverWork, historyOffset) : null,
            Links: historyChanged ? controller.Store.ResultLinks(serverWork) : null), lifetime.Token);
        if (closing || serverWork != IsServer) return;
        var waitingIds = snapshot.Active.Rows.Where(x => x.Job.State == JobState.AwaitingManualAction).Select(x => x.Job.Id).ToHashSet(StringComparer.Ordinal);
        attention.RemoveResolved(waitingIds.Contains);
        RefreshAttention();
        ApplyJobRows(ActiveJobsGrid, snapshot.Active.Rows.Select(JobRow).ToArray());
        if (snapshot.History is not { } history) return;
        WorkspaceJobRow[] rows = history.Rows.Select(JobRow).ToArray();
        ApplyJobRows(JobsGrid, rows); resultLinks = snapshot.Links!;
        historyOffset = history.Offset; historyTotal = history.Total; displayedJobChanges = revision;
        HistoryPageText.Text = history.Total == 0 ? "История пуста" : $"{history.Offset + 1}–{history.Offset + history.Rows.Length} из {history.Total}";
        HistoryPreviousButton.IsEnabled = historyOffset > 0;
        HistoryNextButton.IsEnabled = historyOffset + history.Rows.Length < history.Total;
        // Run choices follow the navigable history page; retain a previously chosen run.
        string? selectedRun = (RunFilterInput.SelectedItem as WorkspaceFilterOption)?.Id;
        CollectionJob? retained = filterJobs.FirstOrDefault(x => x.Id == selectedRun);
        filterJobs = snapshot.Active.Rows.Concat(history.Rows).Select(x => x.Job)
            .Concat(retained == null ? [] : [retained]).DistinctBy(x => x.Id).ToArray();
        RefreshResultFilters();
    }
    private static void ApplyJobRows(DataGrid grid, WorkspaceJobRow[] rows)
    {
        if (grid.ItemsSource is WorkspaceJobRow[] previous && previous.SequenceEqual(rows)) return;
        string? selectedId = (grid.SelectedItem as WorkspaceJobRow)?.Id;
        grid.ItemsSource = rows; grid.SelectedItem = rows.FirstOrDefault(x => x.Id == selectedId);
    }
    private WorkspaceJobRow JobRow(JobProgressRow value)
    {
        CollectionJob job = value.Job;
        string progress = value.IsMap
            ? value.ExpectedCount is int expected ? $"{value.Collected} / {expected} объявл." : $"{value.Collected} объявл."
            : $"{job.Page} / {job.Limit} стр.";
        return new(job.Id, searches.FirstOrDefault(item => item.Id == job.LinkId || item.Url == job.Url)?.Label ?? new Uri(job.Url).Host,
            job.Source.ToString(), value.CompletionKind == CollectionCompletionKind.Partial ? "Неполный сбор" : job.DisplayState,
            progress + (value.SourceCountHint is int count ? $" · источник: {count}" : ""),
            job.StartedAtUtc.ToLocalTime().ToString("dd.MM HH:mm", CultureInfo.CurrentCulture), job.Reason);
    }
    private async void HistoryPreviousClick(object sender, RoutedEventArgs e) => await ActionAsync(async () =>
    { historyOffset = Math.Max(0, historyOffset - 50); await RefreshJobsAsync(true); });
    private async void HistoryNextClick(object sender, RoutedEventArgs e) => await ActionAsync(async () =>
    { if (historyOffset + 50 < historyTotal) historyOffset += 50; await RefreshJobsAsync(true); });
    private async void ModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || changingMode || controller == null) return;
        int chosen = ModeInput.SelectedIndex;
        await ActionAsync(async () =>
        {
            try
            {
                await controller.CloseManualAsync(); controller.SetMode(chosen == 1 ? ParserOperatingMode.Server : ParserOperatingMode.Local);
                remote = null; searches = []; LinksGrid.ItemsSource = searches; GroupsList.ItemsSource = null;
                historyOffset = 0; displayedJobChanges = -1; filterJobs = []; resultLinks = []; ActiveJobsGrid.ItemsSource = null;
                ListingsGrid.ItemsSource = null; JobsGrid.ItemsSource = null; PageText.Text = ""; DetailsPanel.Clear();
                if (detailsWindow != null) { detailsWindow.Close(); detailsWindow = null; }
                await ReloadAsync(); StatusText.Text = "Режим изменён. Поиски остаются в своём рабочем пространстве.";
            }
            finally { changingMode = true; ModeInput.SelectedIndex = IsServer ? 1 : 0; changingMode = false; }
        });
    }
    private async void AutomationClick(object sender, RoutedEventArgs e)
    {
        if (controller == null) return;
        // Stop is immediate even if a server request is still in flight.
        if (controller.AutomationEnabled) { StopClick(sender, e); return; }
        long stopVersion = controller.Runner.StopVersion;
        await ActionAsync(async () =>
        {
            await controller.CloseManualAsync();
            if (stopVersion != controller.Runner.StopVersion) return;
            controller.SetAutomation(true); ShowStatus(); StatusText.Text = "Авторабота включена. Приложение должно оставаться открытым.";
        });
    }
    private void StopClick(object sender, RoutedEventArgs e)
    {
        if (controller == null) return;
        try { controller.SetAutomation(false); StatusText.Text = "Авторабота выключена. Текущий сбор останавливается, результаты сохраняются."; ShowStatus(); }
        catch (Exception ex) { StatusText.Text = FriendlyError(ex); }
    }
    private async void PauseClick(object sender, RoutedEventArgs e) => await ActionAsync(async () =>
    { await Task.Run(() => controller!.Runner.Pause(), lifetime.Token); StatusText.Text = "Текущий сбор на паузе. Чтобы выключить автоработу, нажмите «Остановить всё»."; });
    private async void ResumeClick(object sender, RoutedEventArgs e) => await ActionAsync(() => Task.Run(() => controller!.Runner.Resume(), lifetime.Token));
    private void OnNotice(object? sender, CollectionNotice notice) => _ = Dispatcher.InvokeAsync(() =>
    {
        if (closing) return;
        if (attention.Add(notice, DateTimeOffset.UtcNow)) NotifyAttention(notice);
        RefreshAttention();
    });
    private string AttentionText(ManualAttention alert)
    {
        CollectionNotice notice = alert.Notice;
        string? linkId = controller!.Store.Job(notice.JobId)?.LinkId;
        string label = controller.Store.Links().FirstOrDefault(x => x.Id == linkId || x.Url == notice.Link)?.Label
            ?? searches.FirstOrDefault(x => x.Url == notice.Link)?.Label ?? new Uri(notice.Link).Host;
        string reason = notice.Reason.Split(':', 2)[0] switch
        {
            "Captcha" => "пройдите CAPTCHA",
            "AuthenticationRequired" => "войдите в аккаунт на сайте",
            "RateLimited" => "сайт ограничил запросы",
            _ => "проверьте страницу источника"
        };
        return $"{notice.Source} · {label} — {reason}. Ожидает с {alert.SinceUtc.ToLocalTime():dd.MM HH:mm}.";
    }
    private void RefreshAttention()
    {
        ManualAttention[] pending = attention.Pending;
        NoticePanel.Visibility = pending.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (pending.Length == 0)
        {
            noticeSource = null;
            attentionNotification?.Clear();
            return;
        }
        noticeSource = pending[0].Notice.Source;
        NoticeText.Text = string.Join(Environment.NewLine, pending.Select(AttentionText));
        foreach (var alert in attention.Reminders(DateTimeOffset.UtcNow)) NotifyAttention(alert.Notice);
    }
    private void NotifyAttention(CollectionNotice notice)
    {
        attentionNotification ??= new WindowsAttentionNotification(this, OpenAttention);
        var alert = attention.Pending.First(x => x.Notice.JobId == notice.JobId);
        if (!attentionNotification.Show(AttentionText(alert)))
            StatusText.Text = "Не удалось показать уведомление Windows. Требуется действие — см. плашку сверху.";
    }
    private void OpenAttention()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        _ = ActionAsync(async () =>
        {
            if (attention.Pending.FirstOrDefault() is { } alert)
                await controller!.Runner.ActivateManualPageAsync(alert.Notice.JobId, lifetime.Token);
        });
    }
    private void OpenAttentionClick(object sender, RoutedEventArgs e) => OpenAttention();
    private async void VerifyClick(object sender, RoutedEventArgs e) => await ActionAsync(async () =>
    { if (noticeSource is { } source) { await controller!.Runner.ResumeSourceAsync(source); await RefreshJobsAsync(true); } });
    private async void RefreshClick(object sender, RoutedEventArgs e) => await ActionAsync(ReloadAsync);
    private WorkspaceSearch Selected() => LinksGrid.SelectedItem as WorkspaceSearch ?? throw new InvalidOperationException("Выберите поиск в списке.");
    private async void AddClick(object sender, RoutedEventArgs e) => await ActionAsync(() => EditSearchAsync(null));
    private async void EditClick(object sender, RoutedEventArgs e) => await ActionAsync(() => EditSearchAsync(Selected()));
    private Task EditSearchAsync(WorkspaceSearch? item, string? captured = null)
    {
        WorkspaceGroup[] groups = ((WorkspaceGroup[]?)GroupsList.ItemsSource ?? []).Where(x => !string.IsNullOrEmpty(x.Id)).ToArray();
        SearchDraft draft = new(item?.Label ?? "Новый поиск", captured ?? item?.Url ?? "", item?.GroupId ?? (GroupsList.SelectedItem as WorkspaceGroup)?.Id,
            item?.Schedule ?? new(LocalScheduleKind.Manual), item?.Remote?.MaxPages ?? controller!.Store.Settings().MaxPages);
        if (draft.GroupId == "") draft = draft with { GroupId = null };
        Guid commandId = Guid.CreateVersion7();
        bool areaSaved = false;
        SearchEditorWindow editor = new(item == null ? "Добавить поиск" : "Изменить поиск", draft, groups, IsServer, false, async value =>
        {
            if (IsServer)
            {
                await controller!.ConnectServerAsync(); ListingSource source = Enum.Parse<ListingSource>(SearchUrls.Normalize(value.Url).Source.ToString());
                Guid? group = value.GroupId == null ? null : Guid.Parse(value.GroupId);
                if (item?.Remote is { } old) await controller.Server!.UpdateSearchAsync(new(old.Id, old.Revision, value.Label, source, value.Url, value.MaxPages, group, value.ServerSchedule, old.Enabled), lifetime.Token);
                else await controller.Server!.CreateSearchAsync(new(commandId, value.Label, source, value.Url, value.MaxPages, group, value.ServerSchedule), lifetime.Token);
            }
            else controller!.Store.SaveSearch(value.Label, value.Url, value.GroupId, value.Schedule, item?.Local);
            areaSaved = await controller!.CaptureSearchAreaAsync(value.Url, lifetime.Token);
        }) { Owner = this };
        if (DuringDialog(editor.ShowDialog) == true)
        {
            StatusText.Text = areaSaved ? "Поиск сохранён вместе с эталоном области."
                : captured is null || SearchUrls.Normalize(draft.Url).Source != SourceSite.Avito || AvitoMapData.DrawId(draft.Url) is null ? "Поиск сохранён."
                : "Поиск сохранён. Геометрия области пока не получена; до её подтверждения сбор будет неполным.";
            return LoadSearchesAsync();
        }
        return Task.CompletedTask;
    }
    private async void ToggleSearchClick(object sender, RoutedEventArgs e) => await ActionAsync(async () =>
    {
        WorkspaceSearch item = Selected();
        if (item.Remote is { } r) await controller!.Server!.UpdateSearchAsync(new(r.Id, r.Revision, r.Label, r.Source, r.Url, r.MaxPages, r.GroupId, r.Schedule, !r.Enabled), lifetime.Token);
        else if (item.Local is { } l) controller!.Store.SaveLink(l.Link.Label, l.Link.Url, l.Link.Selected, l.Link.Source, l.Link.Id, !l.Link.Enabled);
        await LoadSearchesAsync();
    });
    private async void ArchiveClick(object sender, RoutedEventArgs e) => await ActionAsync(async () =>
    {
        WorkspaceSearch item = Selected(); if (item.Local == null) return;
        if (MessageBox.Show(this, $"Убрать «{item.Label}» из рабочих поисков? Результаты и история сохранятся.", "Архивировать поиск", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        controller!.Store.ArchiveLink(item.Id); await LoadSearchesAsync();
    });
    private async void RunClick(object sender, RoutedEventArgs e)
    {
        if (controller?.DiagnosticsOnly == true) return;
        long stopVersion = controller?.Runner.StopVersion ?? 0;
        await ActionAsync(async () =>
        {
        WorkspaceSearch item = Selected(); if (!item.Enabled) throw new InvalidOperationException("Сначала включите поиск.");
        await controller!.CloseManualAsync();
        if (item.Remote != null)
        {
            await controller.Server!.EnqueueSearchAsync(item.Remote.Id, lifetime.Token);
            StatusText.Text = "Поиск добавлен в очередь сервера. Его примет доступный парсер с включённой автоработой.";
        }
        else
        {
            var launch = await Task.Run(() => new { Completion = controller.Runner.StartAsync(controller.Store.Settings(), onlyLinkId: item.Id, expectedStopVersion: stopVersion) }, lifetime.Token);
            _ = ObserveRunAsync(launch.Completion);
            StatusText.Text = "Сбор запущен с учётом настройки свежести.";
        }
        Pages.SelectedIndex = 1; await RefreshJobsAsync(true);
        });
    }
    private async Task ObserveRunAsync(Task run)
    { try { await run; } catch (Exception ex) { if (!closing) StatusText.Text = FriendlyError(ex); } }
    private async void BrowseClick(object sender, RoutedEventArgs e) => await ActionAsync(async () =>
    {
        if (controller!.Runner.IsRunning) throw new InvalidOperationException("Сначала остановите текущий сбор.");
        WorkspaceSearch? selected = LinksGrid.SelectedItem as WorkspaceSearch;
        string url;
        if (selected != null) url = selected.Url;
        else
        {
            string? selectedUrl = DuringDialog(() => SearchEditorWindow.ChooseSource(this));
            if (selectedUrl == null) return;
            url = selectedUrl;
        }
        await OpenBrowserAsync(url); StatusText.Text = "Настройте область и фильтры в браузере, затем нажмите «Добавить из браузера».";
    });
    private async Task OpenBrowserAsync(string url)
    {
        if (controller!.Runner.IsRunning) throw new InvalidOperationException("Сначала остановите текущий сбор.");
        NormalizedSearch safe = SearchUrls.Normalize(url);
        await controller.OpenManualAsync(new("manual", "Браузер", safe.Url, safe.Source, false, false, 1));
    }
    private async void MapDiagnosticOpenClick(object sender, RoutedEventArgs e) => await ActionAsync(async () =>
    {
        WorkspaceSearch? item = LinksGrid.SelectedItem as WorkspaceSearch;
        string url = string.IsNullOrWhiteSpace(MapDiagnosticUrlInput.Text) ? item?.Url ?? "" : MapDiagnosticUrlInput.Text.Trim();
        if (url.Length == 0) throw new InvalidOperationException("Выберите поиск или вставьте ссылку Avito в поле рядом с кнопкой.");
        NormalizedSearch safe = SearchUrls.Normalize(url, SourceSite.Avito);
        string folder = await controller!.BeginMapDiagnosticsAsync(new(item?.Id ?? "manual-diagnostic", item?.Label ?? "Диагностика карты", safe.Url,
            safe.Source, false, false, 1), lifetime.Token);
        StatusText.Text = "Дождитесь загрузки карты, не меняйте масштаб. Затем нажмите «Сохранить диагностику карты». Папка: " + folder;
    });
    private async void MapDiagnosticSaveClick(object sender, RoutedEventArgs e) => await ActionAsync(async () =>
    {
        string folder = await controller!.FinishMapDiagnosticsAsync(lifetime.Token);
        StatusText.Text = "Диагностика сохранена: " + folder;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    });
    private async void CaptureClick(object sender, RoutedEventArgs e) => await ActionAsync(async () =>
    {
        if (controller!.DiagnosticsOnly) throw new InvalidOperationException("Используйте «Сохранить диагностику карты».");
        var current = controller!.CurrentManualSearch() ?? throw new InvalidOperationException("Сначала откройте сайт кнопкой «Открыть сайт» и настройте поиск.");
        await EditSearchAsync(null, current.Url);
    });
    private async void SaveSearchAreaClick(object sender, RoutedEventArgs e) => await ActionAsync(async () =>
    {
        WorkspaceSearch selected = Selected();
        if (controller!.Runner.IsRunning) throw new InvalidOperationException("Сначала остановите текущий сбор.");
        if (controller.CurrentManualSearch() is null) throw new InvalidOperationException("Откройте выбранный поиск кнопкой «Открыть сайт» и проверьте выделенную область.");
        bool saved = await controller.CaptureSearchAreaAsync(selected.Url, lifetime.Token, explicitlyConfirmVisibleArea: true);
        StatusText.Text = saved ? "Эталон области сохранён для выбранного поиска на этом компьютере."
            : "Область не сохранена: геометрия недоступна, фильтры отличаются или существующий эталон не совпадает. Эталон не изменён.";
    });
    private async void TransferClick(object sender, RoutedEventArgs e) => await ActionAsync(async () =>
    {
        WorkspaceSearch selected = Selected(); if (selected.Local == null) return;
        await controller!.ConnectServerAsync(); CollectorWorkspace workspace = await controller.Server!.ReadWorkspaceAsync(lifetime.Token);
        WorkspaceGroup[] groups = workspace.Groups.Where(x => x.Active).Select(x => new WorkspaceGroup(x.Id.ToString(), x.Name)).ToArray();
        if (groups.Length == 0) throw new InvalidOperationException("Сначала создайте группу в серверном режиме или в LandErp.");
        Guid commandId = Guid.CreateVersion7();
        SearchEditorWindow dialog = new("Добавить ссылку на сервер", new(selected.Label, selected.Url, null, new(LocalScheduleKind.Manual), controller.Store.Settings().MaxPages), groups, true, true, async draft =>
        {
            string groupName = groups.Single(x => x.Id == draft.GroupId).Name;
            if (MessageBox.Show(this, $"Добавить «{draft.Label}» в группу «{groupName}» на сервере {controller.SavedServerConnection()?.Origin.Host}?\n\n{draft.Url}\n\nЛокальные группы, расписания, история и результаты не передаются. Новое расписание — из этого окна.", "Подтвердите добавление", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) throw new OperationCanceledException();
            await controller.Server.CreateSearchAsync(new(commandId, draft.Label, Enum.Parse<ListingSource>(SearchUrls.Normalize(draft.Url).Source.ToString()), draft.Url, draft.MaxPages, Guid.Parse(draft.GroupId!), draft.ServerSchedule), lifetime.Token);
        }) { Owner = this };
        if (DuringDialog(dialog.ShowDialog) == true) StatusText.Text = "Ссылка добавлена на сервер. Локальный поиск сохранён без изменений.";
    });
    private async void AddGroupClick(object sender, RoutedEventArgs e) => await ActionAsync(() => SaveGroupAsync(false, false));
    private async void RenameGroupClick(object sender, RoutedEventArgs e) => await ActionAsync(() => SaveGroupAsync(true, false));
    private async void ArchiveGroupClick(object sender, RoutedEventArgs e) => await ActionAsync(() => SaveGroupAsync(true, true));
    private async Task SaveGroupAsync(bool edit, bool archive)
    {
        WorkspaceGroup? selected = GroupsList.SelectedItem as WorkspaceGroup;
        if (edit && string.IsNullOrEmpty(selected?.Id)) throw new InvalidOperationException("Выберите группу.");
        string? name = archive ? selected!.Name : DuringDialog(() => SearchEditorWindow.AskName(this, edit ? "Переименовать группу" : "Новая группа", edit ? selected!.Name : ""));
        if (name == null) return;
        if (archive && MessageBox.Show(this, $"Архивировать группу «{name}»? Поиски и результаты не удаляются.", "Архивировать группу", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        if (IsServer)
        {
            await controller!.ConnectServerAsync();
            if (edit)
            {
                CollectorGroupView old = remote!.Groups.Single(x => x.Id.ToString() == selected!.Id);
                await controller.Server!.UpdateGroupAsync(new(old.Id, old.Revision, name, old.SortOrder, !archive), lifetime.Token);
            }
            else await controller.Server!.CreateGroupAsync(new(Guid.CreateVersion7(), name, 100), lifetime.Token);
        }
        else
        {
            LocalGroup? old = edit ? controller!.Store.Groups().Single(x => x.Id == selected!.Id) : null;
            if (archive && searches.Any(x => x.GroupId == selected!.Id && x.Enabled)) throw new InvalidOperationException("Сначала перенесите или приостановите поиски этой группы.");
            controller!.Store.SaveGroup(name, old?.SortOrder ?? 100, old?.Id, !archive, old?.Revision);
        }
        await LoadSearchesAsync();
    }
    private async void ConnectClick(object sender, RoutedEventArgs e) => await ActionAsync(async () =>
    { if (new ServerConnectionWindow(controller!) { Owner = this }.ShowDialog() == true) { remote = null; StatusText.Text = "Сервер подключён."; if (IsServer) await ReloadAsync(); } });
    private async void ResetServerSyncClick(object sender, RoutedEventArgs e) => await ActionAsync(async () =>
    {
        if (!IsServer) throw new InvalidOperationException("Переключитесь в режим «Через сервер».");
        if (MessageBox.Show(this,
            "Будет очищена только локальная очередь отправки и текущая серверная работа на этом компьютере.\n\nЛокальные поиски, история и результаты останутся в базе. Продолжить?",
            "Сбросить серверную синхронизацию", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await controller!.ResetServerSynchronizationAsync(lifetime.Token);
        remote = null; await RefreshJobsAsync(true); ShowStatus();
        StatusText.Text = "Серверная синхронизация сброшена. Локальные поиски, история и результаты сохранены.";
    });
    private async void SaveSettingsClick(object sender, RoutedEventArgs e) => await ActionAsync(() =>
    {
        if (!int.TryParse(MaxPagesInput.Text, out int pages) || pages is < 1 or > 100) throw new ArgumentException("Предел страниц: от 1 до 100.");
        if (!double.TryParse(FreshnessInput.Text, out double hours) || !double.IsFinite(hours) || hours is < 0 or > 168) throw new ArgumentException("Свежесть данных: от 0 до 168 часов.");
        controller!.Store.SaveSettings(controller.Store.Settings() with { Browser = BrowserInput.SelectedIndex == 1 ? "msedge" : "chrome", MaxPages = pages, FreshnessHours = hours });
        controller.SetListingDetailsMode(ListingDetailsModeInput.SelectedIndex == 1 ? ListingDetailsDisplayMode.SeparateWindow : ListingDetailsDisplayMode.SidePanel);
        ApplyListingDetailsMode();
        StatusText.Text = "Настройки сохранены для следующего запуска."; return Task.CompletedTask;
    });
    private ListingFilter Filter(int start = 0, int size = 100)
    {
        SourceSite? source = (SourceFilterInput.SelectedItem as WorkspaceFilterOption)?.Id is string sourceId && Enum.TryParse(sourceId, out SourceSite parsedSource) ? parsedSource : null;
        string? linkId = (SearchFilterInput.SelectedItem as WorkspaceFilterOption)?.Id;
        string? jobId = (RunFilterInput.SelectedItem as WorkspaceFilterOption)?.Id;
        ListingQualityFilter quality = (QualityFilterInput.SelectedItem as WorkspaceFilterOption)?.Id is string qualityId
            && Enum.TryParse(qualityId, out ListingQualityFilter parsedQuality) ? parsedQuality : ListingQualityFilter.All;
        return new(SearchInput.Text, source, linkId, jobId, start, size, false, IsServer, quality);
    }
    private async Task FindAsync()
    {
        ListingRow? selected = ListingsGrid.SelectedItem as ListingRow;
        ListingFilter filter = Filter(offset);
        ListingPage page = await Task.Run(() => controller!.Store.ReadListings(filter), lifetime.Token);
        if (closing || filter.ServerWork != IsServer) return;
        total = page.Total;
        ListingsGrid.ItemsSource = page.Rows; PageText.Text = $"{(page.Rows.Length == 0 ? 0 : offset + 1)}–{offset + page.Rows.Length} из {total}";
        ListingsGrid.SelectedItem = page.Rows.FirstOrDefault(item => item.ExternalId == selected?.ExternalId && item.Source == selected.Source);
    }
    private async void FindClick(object sender, RoutedEventArgs e) => await ActionAsync(() => { offset = 0; return FindAsync(); });
    private async void ResetFiltersClick(object sender, RoutedEventArgs e) => await ActionAsync(() =>
    {
        SearchInput.Clear();
        SourceFilterInput.SelectedIndex = 0;
        SearchFilterInput.SelectedIndex = 0;
        RunFilterInput.SelectedIndex = 0;
        QualityFilterInput.SelectedIndex = 0;
        offset = 0;
        return FindAsync();
    });
    private async void PreviousClick(object sender, RoutedEventArgs e) => await ActionAsync(() => { offset = Math.Max(0, offset - 100); return FindAsync(); });
    private async void NextClick(object sender, RoutedEventArgs e) => await ActionAsync(() => { if (offset + 100 < total) offset += 100; return FindAsync(); });
    private async void ListingSelected(object sender, SelectionChangedEventArgs e) => await ShowSelectedListingAsync();
    private async Task ShowSelectedListingAsync()
    {
        long request = ++detailsRequest;
        if (controller == null || ListingsGrid.SelectedItem is not ListingRow row)
        {
            DetailsPanel.Clear(); detailsWindow?.Clear(); return;
        }
        bool serverWork = IsServer;
        try
        {
            var detail = await Task.Run(() =>
            {
                HistoryRow[] history = controller.Store.History(row.Source, row.ExternalId, serverWork: serverWork);
                CollectionJob? job = history.Length == 0 ? null : controller.Store.Job(history[0].JobId);
                return (History: history, Job: job, Hint: job == null ? null : controller.Store.Completion(job.Id)?.SourceCountHint);
            }, lifetime.Token);
            // A slow read must not replace a newer selection or a different workspace.
            if (closing || request != detailsRequest || serverWork != IsServer) return;
            HistoryRow[] history = detail.History;
            string context = ListingContext(history, detail.Job, detail.Hint);
            if (controller.ListingDetailsMode == ListingDetailsDisplayMode.SeparateWindow)
            {
                DetailsPanel.Clear();
                ListingDetailsWindow window = EnsureDetailsWindow();
                window.ShowListing(row, history, context);
            }
            else DetailsPanel.ShowListing(row, history, context);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { if (!closing && request == detailsRequest) StatusText.Text = FriendlyError(ex); }
    }
    private string ListingContext(HistoryRow[] history, CollectionJob? job, int? hint)
    {
        WorkspaceController? activeController = controller;
        if (activeController == null || history.Length == 0) return "Контекст запуска не найден.";
        HistoryRow latest = history[0];
        if (job == null) return $"Job: {latest.JobId}\nСтраница: {latest.Page}";
        string label = searches.FirstOrDefault(x => x.Id == job.LinkId || x.Url == job.Url)?.Label ?? new Uri(job.Url).Host;
        return $"Поиск: {label}\nJob: {job.Id}\nСтраница: {latest.Page}\nPass: {job.PassId}" +
            (hint is int count ? $"\nОбъявлений по данным источника: {count}" : "");
    }
    private ListingDetailsWindow EnsureDetailsWindow()
    {
        if (detailsWindow != null) return detailsWindow;
        ListingDetailsWindow window = new() { Owner = this };
        window.OpenListingRequested += OpenListingRequested;
        window.Closed += (_, _) => { if (ReferenceEquals(detailsWindow, window)) detailsWindow = null; };
        detailsWindow = window; window.Show(); return window;
    }
    private void OpenListingRequested(object? sender, ListingOpenEventArgs e) => _ = ActionAsync(() => OpenBrowserAsync(e.Url));
    private void ApplyListingDetailsMode()
    {
        if (controller == null) return;
        bool side = controller.ListingDetailsMode == ListingDetailsDisplayMode.SidePanel;
        DetailsPanel.Visibility = side ? Visibility.Visible : Visibility.Collapsed;
        DetailsSplitter.Visibility = side ? Visibility.Visible : Visibility.Collapsed;
        DetailsSplitterColumn.Width = side ? new GridLength(6) : new GridLength(0);
        DetailsColumn.MinWidth = side ? 320 : 0;
        DetailsColumn.Width = side ? new GridLength(controller.ListingDetailsWidth) : new GridLength(0);
        if (side && detailsWindow != null) { detailsWindow.Close(); detailsWindow = null; }
        if (!side && ListingsGrid.SelectedItem is ListingRow) _ = ShowSelectedListingAsync();
    }
    private void DetailsSplitterDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (controller?.ListingDetailsMode != ListingDetailsDisplayMode.SidePanel) return;
        controller.SetListingDetailsWidth(DetailsColumn.ActualWidth);
        DetailsColumn.Width = new GridLength(controller.ListingDetailsWidth);
    }
    private void InitializeResultFilters()
    {
        SourceFilterInput.ItemsSource = new WorkspaceFilterOption[] { new(null, "Все источники"), new(nameof(SourceSite.Avito), "Avito"), new(nameof(SourceSite.Cian), "Cian") };
        SourceFilterInput.SelectedIndex = 0;
        QualityFilterInput.ItemsSource = new WorkspaceFilterOption[]
        {
            new(null, "Все результаты"),
            new(nameof(ListingQualityFilter.Warnings), "Только с предупреждениями"),
            new(nameof(ListingQualityFilter.LandTypeConflict), "Конфликт типа участка"),
            new(nameof(ListingQualityFilter.MissingPrice), "Нет цены"),
            new(nameof(ListingQualityFilter.MissingArea), "Нет площади"),
            new(nameof(ListingQualityFilter.MissingCoordinates), "Нет координат"),
            new(nameof(ListingQualityFilter.MissingSourcePublishedAt), "Нет даты публикации"),
            new(nameof(ListingQualityFilter.MissingCadastralNumber), "Нет кадастрового номера")
        };
        QualityFilterInput.SelectedIndex = 0;
        SearchFilterInput.ItemsSource = new WorkspaceFilterOption[] { new(null, "Все поиски") }; SearchFilterInput.SelectedIndex = 0;
        RunFilterInput.ItemsSource = new WorkspaceFilterOption[] { new(null, "Все запуски") }; RunFilterInput.SelectedIndex = 0;
    }
    private void RefreshResultFilters()
    {
        if (controller == null || SearchFilterInput == null) return;
        string? searchId = (SearchFilterInput.SelectedItem as WorkspaceFilterOption)?.Id;
        string? runId = (RunFilterInput.SelectedItem as WorkspaceFilterOption)?.Id;
        CollectionJob[] jobs = filterJobs;
        WorkspaceFilterOption[] searchOptions = [new(null, "Все поиски"), .. resultLinks.Select(x => new WorkspaceFilterOption(x.Id, x.Label)).Concat(jobs.GroupBy(x => x.LinkId).Select(group =>
        {
            CollectionJob item = group.OrderByDescending(x => x.StartedAtUtc).First();
            string label = searches.FirstOrDefault(x => x.Id == item.LinkId || x.Url == item.Url)?.Label ?? new Uri(item.Url).Host;
            return new WorkspaceFilterOption(item.LinkId, label);
        })).DistinctBy(x => x.Id).OrderBy(x => x.Label)];
        WorkspaceFilterOption[] runOptions = [new(null, "Все запуски"), .. jobs.OrderByDescending(x => x.StartedAtUtc)
            .Select(x => new WorkspaceFilterOption(x.Id, $"{x.StartedAtUtc.ToLocalTime():dd.MM HH:mm} · {searches.FirstOrDefault(s => s.Id == x.LinkId || s.Url == x.Url)?.Label ?? new Uri(x.Url).Host}"))];
        SearchFilterInput.ItemsSource = searchOptions; SearchFilterInput.SelectedItem = searchOptions.FirstOrDefault(x => x.Id == searchId) ?? searchOptions[0];
        RunFilterInput.ItemsSource = runOptions; RunFilterInput.SelectedItem = runOptions.FirstOrDefault(x => x.Id == runId) ?? runOptions[0];
    }

    public static string CsvCell(string? text)
    {
        string value = text ?? "";
        if (value.TrimStart().StartsWith('=') || value.TrimStart().StartsWith('+') || value.TrimStart().StartsWith('-') || value.TrimStart().StartsWith('@')) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
    private async void ExportClick(object sender, RoutedEventArgs e) => await ActionAsync(async () =>
    {
        SaveFileDialog dialog = new() { Filter = "Таблица CSV|*.csv", FileName = "Объявления.csv" };
        if (DuringDialog(() => dialog.ShowDialog(this)) != true) return;
        ListingFilter filter = Filter(size: 500);
        await Task.Run(async () =>
        {
        using StreamWriter writer = new(dialog.FileName, false, new UTF8Encoding(true));
        await writer.WriteLineAsync("Источник;ID;Название;Цена;Площадь;Цена за сотку;Адрес;Типы участка;Конфликт типа;Дата публикации источника;Кадастровый номер;Координаты WGS84;Фото;Продавец;Предупреждения;Ссылка;Первое наблюдение;Последнее наблюдение");
        for (int start = 0; ; start += 500)
        {
            lifetime.Token.ThrowIfCancellationRequested();
            ListingPage page = controller!.Store.ReadListings(filter with { Offset = start });
            foreach (ListingRow row in page.Rows)
                await writer.WriteLineAsync(string.Join(';', new[]
                {
                    row.Source.ToString(), row.ExternalId, row.Title, row.Price, row.Area, row.PricePerSotka, row.Location,
                    row.LandTypes, row.LandTypeIssue, row.Published, row.CadastralNumber, row.Coordinates,
                    row.PhotoCount.ToString(CultureInfo.InvariantCulture), row.Seller, row.WarningSummary, row.Observation.Url,
                    row.FirstSeen.ToString("O"), row.LastSeen.ToString("O")
                }.Select(CsvCell)));
            if (start + page.Rows.Length >= page.Total) break;
        }
        }, lifetime.Token);
        StatusText.Text = "Таблица сохранена.";
    });
    private async void OpenDiagnosticsClick(object sender, RoutedEventArgs e) => await ActionAsync(() =>
    {
        if (controller == null) return Task.CompletedTask;
        Directory.CreateDirectory(controller.Diagnostics.RootPath);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{controller.Diagnostics.RootPath}\"") { UseShellExecute = true });
        return Task.CompletedTask;
    });

    private async void ExportTodayDiagnosticsClick(object sender, RoutedEventArgs e) => await ActionAsync(async () =>
    {
        SaveFileDialog dialog = new() { Filter = "ZIP-архив|*.zip", FileName = $"LandErp-диагностика-{DateTime.Now:yyyy-MM-dd}.zip" };
        if (DuringDialog(() => dialog.ShowDialog(this)) != true) return;
        string summary = $"LandErp Parser{Environment.NewLine}Дата: {DateTimeOffset.Now:O}{Environment.NewLine}Версия: {typeof(WorkspaceWindow).Assembly.GetName().Version}{Environment.NewLine}Режим: {(IsServer ? "Сервер" : "Локально")}{Environment.NewLine}Авторабота: {controller!.AutomationEnabled}{Environment.NewLine}Сбор активен: {controller.Runner.IsRunning}{Environment.NewLine}";
        await Task.Run(() => controller.Diagnostics.ExportToday(dialog.FileName, summary + JobSummary()), lifetime.Token);
        StatusText.Text = "Диагностика за сегодня сохранена.";
    });

    private async void DiagnosticsClick(object sender, RoutedEventArgs e) => await ActionAsync(async () =>
    {
        SaveFileDialog dialog = new() { Filter = "Текстовый отчёт|*.txt", FileName = "Parser-диагностика.txt" };
        if (DuringDialog(() => dialog.ShowDialog(this)) != true) return;
        // No tokens, URLs, browser responses or raw source content enter the support report.
        string report = $"LandErp Parser\nДата: {DateTimeOffset.Now:O}\nВерсия: {typeof(WorkspaceWindow).Assembly.GetName().Version}\nРежим: {(IsServer ? "Сервер" : "Локально")}\nАвторабота: {controller!.AutomationEnabled}\nСбор активен: {controller.Runner.IsRunning}\n";
        report += await Task.Run(JobSummary, lifetime.Token);
        await File.WriteAllTextAsync(dialog.FileName, report); StatusText.Text = "Краткая диагностика сохранена.";
    });
    private string JobSummary() => string.Join(Environment.NewLine, controller!.Store.JobStateCounts().Select(x => $"{x.Key}: {x.Value}"));
}
