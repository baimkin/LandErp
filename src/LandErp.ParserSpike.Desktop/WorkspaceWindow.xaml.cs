using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
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
    private SourceSite? noticeSource;
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
                this.controller ??= new(Path.Combine(root, "local-data", "spike-002.sqlite"), Path.Combine(root, "browser-profiles"));
                this.controller.Runner.ManualActionRequired += OnNotice;
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
            if (!ready || closing || ticking) return;
            bool ownsOperation = await operations.WaitAsync(0);
            if (!ownsOperation && !modalOpen) return;
            ticking = true;
            try
            {
                await this.controller!.TickAsync(lifetime.Token);
                if (ownsOperation)
                {
                    if (IsServer && remote == null && this.controller.Server != null && DateTimeOffset.UtcNow >= workspaceReadAfter)
                    { workspaceReadAfter = DateTimeOffset.UtcNow.AddSeconds(30); await LoadSearchesAsync(); }
                    RefreshJobs(); ShowStatus();
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
            await operations.WaitAsync();
            while (ticking) await Task.Delay(20);
            try
            {
                if (detailsWindow != null) { detailsWindow.Close(); detailsWindow = null; }
                if (this.controller != null)
                {
                    this.controller.Runner.ManualActionRequired -= OnNotice;
                    await this.controller.DisposeAsync();
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
        StartButton.IsEnabled = !controller.Runner.IsRunning;
    }
    private async Task ReloadAsync() { await LoadSearchesAsync(); RefreshJobs(); offset = 0; await FindAsync(); }
    private async Task LoadSearchesAsync()
    {
        string? selectedSearch = (LinksGrid.SelectedItem as WorkspaceSearch)?.Id;
        string? groupId = (GroupsList.SelectedItem as WorkspaceGroup)?.Id;
        WorkspaceGroup[] groups;
        if (IsServer)
        {
            await controller!.ConnectServerAsync(); remote = await controller.Server!.ReadWorkspaceAsync(lifetime.Token);
            groups = remote.Groups.Where(x => x.Active).Select(x => new WorkspaceGroup(x.Id.ToString(), x.Name)).ToArray();
            searches = remote.Searches.Select(x => new WorkspaceSearch(x.Id.ToString(), x.Label, x.Url, x.Source.ToString(), x.GroupId?.ToString(),
                new((LocalScheduleKind)x.Schedule.Kind, x.Schedule.IntervalMinutes, x.Schedule.FixedTimes), x.Enabled, Remote: x)).ToArray();
        }
        else
        {
            groups = controller!.Store.Groups().Where(x => x.Active).Select(x => new WorkspaceGroup(x.Id, x.Name)).ToArray();
            searches = controller.Store.ScheduledLinks().Select(x => new WorkspaceSearch(x.Link.Id, x.Link.Label, x.Link.Url, x.Link.Source.ToString(), x.GroupId, x.Schedule, x.Link.Enabled, x)).ToArray();
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
    private void RefreshJobs()
    {
        if (controller == null) return;
        CollectionJob[] jobs = controller.Store.Jobs().Where(x => x.LinkId.StartsWith("server-", StringComparison.Ordinal) == IsServer).ToArray();
        JobsGrid.ItemsSource = jobs.Select(x =>
        {
            int? hint = controller.Store.Completion(x.Id)?.SourceCountHint;
            return new
            {
                Label = searches.FirstOrDefault(item => item.Id == x.LinkId || item.Url == x.Url)?.Label ?? new Uri(x.Url).Host,
                Source = x.Source.ToString(), State = x.DisplayState,
                Progress = JobProgress(x) + (hint is int count ? $" · источник: {count}" : ""),
                Started = x.StartedAtUtc.ToLocalTime().ToString("dd.MM HH:mm", CultureInfo.CurrentCulture)
            };
        }).ToArray();
        RefreshResultFilters(jobs);
    }
    private string JobProgress(CollectionJob job)
    {
        MapScope? map = controller!.Store.ReadMapScope(job.Id);
        if (map != null)
        {
            int collected = controller.Store.Journal(job.Id).Select(item => item.Count).DefaultIfEmpty().Max();
            return map.ExpectedCount is int expected ? $"{collected} / {expected} объявл." : $"{collected} объявл.";
        }
        return $"{job.Page} / {job.Limit} стр.";
    }
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
        await ActionAsync(async () => { await controller.CloseManualAsync(); controller.SetAutomation(true); ShowStatus(); StatusText.Text = "Авторабота включена. Приложение должно оставаться открытым."; });
    }
    private void StopClick(object sender, RoutedEventArgs e)
    {
        if (controller == null) return;
        try { controller.SetAutomation(false); StatusText.Text = "Авторабота выключена. Текущий сбор останавливается, результаты сохраняются."; ShowStatus(); }
        catch (Exception ex) { StatusText.Text = FriendlyError(ex); }
    }
    private void PauseClick(object sender, RoutedEventArgs e) { controller?.Runner.Pause(); StatusText.Text = "Текущий сбор на паузе. Чтобы выключить автоработу, нажмите «Остановить всё»."; }
    private void ResumeClick(object sender, RoutedEventArgs e) => controller?.Runner.Resume();
    private void OnNotice(object? sender, CollectionNotice notice) => _ = Dispatcher.InvokeAsync(() =>
    { noticeSource = notice.Source; NoticePanel.Visibility = Visibility.Visible; NoticeText.Text = $"{notice.Source}: требуется ваше действие. Проверьте открытую вкладку браузера, затем продолжите сбор."; });
    private async void VerifyClick(object sender, RoutedEventArgs e) => await ActionAsync(async () =>
    { if (noticeSource is { } source) { await controller!.Runner.ResumeSourceAsync(source); NoticePanel.Visibility = Visibility.Collapsed; } });
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
        }) { Owner = this };
        if (DuringDialog(editor.ShowDialog) == true) { StatusText.Text = "Поиск сохранён."; return LoadSearchesAsync(); }
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
    private async void RunClick(object sender, RoutedEventArgs e) => await ActionAsync(async () =>
    {
        WorkspaceSearch item = Selected(); if (!item.Enabled) throw new InvalidOperationException("Сначала включите поиск.");
        await controller!.CloseManualAsync();
        if (item.Remote != null)
        {
            await controller.Server!.EnqueueSearchAsync(item.Remote.Id, lifetime.Token);
            StatusText.Text = "Поиск добавлен в очередь сервера. Его примет доступный парсер с включённой автоработой.";
        }
        else { _ = ObserveRunAsync(controller.Runner.StartAsync(controller.Store.Settings(), onlyLinkId: item.Id)); StatusText.Text = "Сбор запущен с учётом настройки свежести."; }
        Pages.SelectedIndex = 1; RefreshJobs();
    });
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
    private async void CaptureClick(object sender, RoutedEventArgs e) => await ActionAsync(async () =>
    {
        var current = controller!.CurrentManualSearch() ?? throw new InvalidOperationException("Сначала откройте сайт кнопкой «Открыть сайт» и настройте поиск.");
        await EditSearchAsync(null, current.Url);
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
    private Task FindAsync()
    {
        string? selectedId = (ListingsGrid.SelectedItem as ListingRow)?.ExternalId;
        ListingPage page = controller!.Store.ReadListings(Filter(offset)); total = page.Total;
        ListingsGrid.ItemsSource = page.Rows; PageText.Text = $"{(page.Rows.Length == 0 ? 0 : offset + 1)}–{offset + page.Rows.Length} из {total}";
        ListingsGrid.SelectedItem = page.Rows.FirstOrDefault(item => item.ExternalId == selectedId);
        return Task.CompletedTask;
    }
    private async void FindClick(object sender, RoutedEventArgs e) => await ActionAsync(() => { offset = 0; return FindAsync(); });
    private async void PreviousClick(object sender, RoutedEventArgs e) => await ActionAsync(() => { offset = Math.Max(0, offset - 100); return FindAsync(); });
    private async void NextClick(object sender, RoutedEventArgs e) => await ActionAsync(() => { if (offset + 100 < total) offset += 100; return FindAsync(); });
    private void ListingSelected(object sender, SelectionChangedEventArgs e) => ShowSelectedListing();
    private void ShowSelectedListing()
    {
        if (controller == null || ListingsGrid.SelectedItem is not ListingRow row)
        {
            DetailsPanel.Clear(); detailsWindow?.Clear(); return;
        }
        HistoryRow[] history = controller.Store.History(row.Source, row.ExternalId, serverWork: IsServer);
        string context = ListingContext(history);
        if (controller.ListingDetailsMode == ListingDetailsDisplayMode.SeparateWindow)
        {
            DetailsPanel.Clear();
            ListingDetailsWindow window = EnsureDetailsWindow();
            window.ShowListing(row, history, context);
        }
        else DetailsPanel.ShowListing(row, history, context);
    }
    private string ListingContext(HistoryRow[] history)
    {
        if (controller == null || history.Count == 0) return "Контекст запуска не найден.";
        HistoryRow latest = history[0];
        CollectionJob? job = controller.Store.Jobs().FirstOrDefault(x => x.Id == latest.JobId);
        if (job == null) return $"Job: {latest.JobId}\nСтраница: {latest.Page}";
        string label = searches.FirstOrDefault(x => x.Id == job.LinkId || x.Url == job.Url)?.Label ?? new Uri(job.Url).Host;
        int? hint = controller.Store.Completion(job.Id)?.SourceCountHint;
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
        DetailsGapColumn.Width = side ? new GridLength(20) : new GridLength(0);
        DetailsColumn.Width = side ? new GridLength(2, GridUnitType.Star) : new GridLength(0);
        if (side && detailsWindow != null) { detailsWindow.Close(); detailsWindow = null; }
        if (!side && ListingsGrid.SelectedItem is ListingRow) ShowSelectedListing();
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
    private void RefreshResultFilters(CollectionJob[]? knownJobs = null)
    {
        if (controller == null || SearchFilterInput == null) return;
        string? searchId = (SearchFilterInput.SelectedItem as WorkspaceFilterOption)?.Id;
        string? runId = (RunFilterInput.SelectedItem as WorkspaceFilterOption)?.Id;
        CollectionJob[] jobs = knownJobs ?? controller.Store.Jobs().Where(x => x.LinkId.StartsWith("server-", StringComparison.Ordinal) == IsServer).ToArray();
        WorkspaceFilterOption[] searchOptions = [new(null, "Все поиски"), .. jobs.GroupBy(x => x.LinkId).Select(group =>
        {
            CollectionJob item = group.OrderByDescending(x => x.StartedAtUtc).First();
            string label = searches.FirstOrDefault(x => x.Id == item.LinkId || x.Url == item.Url)?.Label ?? new Uri(item.Url).Host;
            return new WorkspaceFilterOption(item.LinkId, label);
        }).OrderBy(x => x.Label)];
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
        using StreamWriter writer = new(dialog.FileName, false, new UTF8Encoding(true));
        await writer.WriteLineAsync("Источник;ID;Название;Цена;Площадь;Цена за сотку;Адрес;Типы участка;Конфликт типа;Дата публикации источника;Кадастровый номер;Координаты WGS84;Фото;Продавец;Предупреждения;Ссылка;Первое наблюдение;Последнее наблюдение");
        for (int start = 0; ; start += 500)
        {
            ListingPage page = controller!.Store.ReadListings(Filter(start, 500));
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
        StatusText.Text = "Таблица сохранена.";
    });
    private async void DiagnosticsClick(object sender, RoutedEventArgs e) => await ActionAsync(async () =>
    {
        SaveFileDialog dialog = new() { Filter = "Текстовый отчёт|*.txt", FileName = "Parser-диагностика.txt" };
        if (DuringDialog(() => dialog.ShowDialog(this)) != true) return;
        // No tokens, URLs, browser responses or raw source content enter the support report.
        string report = $"LandErp Parser\nДата: {DateTimeOffset.Now:O}\nВерсия: {typeof(WorkspaceWindow).Assembly.GetName().Version}\nРежим: {(IsServer ? "Сервер" : "Локально")}\nАвторабота: {controller!.AutomationEnabled}\nСбор активен: {controller.Runner.IsRunning}\n";
        report += string.Join("\n", controller.Store.Jobs().GroupBy(x => x.State).Select(x => $"{x.Key}: {x.Count()}"));
        await File.WriteAllTextAsync(dialog.FileName, report); StatusText.Text = "Краткая диагностика сохранена.";
    });
}
