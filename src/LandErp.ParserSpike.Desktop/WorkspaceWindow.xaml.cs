using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LandErp.ParserSpike.LocalCollection;
using LandErp.ParserSpike.ServerIntegration;
using LandErp.Collector.Contracts.V1;
using Microsoft.Win32;

namespace LandErp.ParserSpike.Desktop;

/// <summary>Presentation only: all collection lifetime and durable operations belong to the controller.</summary>
public partial class WorkspaceWindow : Window
{
    private WorkspaceController? controller;
    private readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromSeconds(1) };
    private string? editing;
    private int offset;
    private int historyOffset;
    private bool closing;
    private bool reading;
    private string? selectedJob;
    private Task selectionUpdate = Task.CompletedTask;
    private bool starting;
    private bool serverCommand;
    private bool serverTick;
    private async Task ServerActionAsync(Func<Task> action)
    {
        if (controller == null || serverCommand) return;
        serverCommand = true;
        try { await action(); ServerStatusText.Text = controller.Server?.Status ?? "Server подключён"; }
        catch (ServerDeliveryException exception)
        {
            ServerStatusText.Text = exception.Code switch
            {
                CollectorErrorCodes.SearchPermissionRequired => "В ERP для этого Parser нужно включить право «Разрешить добавление».",
                CollectorErrorCodes.AgentUnauthorized => "Подключение отозвано или ключ изменён. Вставьте новый код подключения из ERP.",
                "SERVER_UNAVAILABLE" or "SERVER_TIMEOUT" => "Server сейчас недоступен. Локальные данные сохранены; повторите позже.",
                _ => "Server отклонил действие: " + exception.Code
            };
        }
        catch (Exception) { ServerStatusText.Text = "Server действие не выполнено. Проверьте локальные настройки/HTTPS и повторите. Local mode и данные сохранены."; }
        finally { serverCommand = false; }
    }
    private void ConnectServerClick(object sender, RoutedEventArgs e)
    {
        if (controller == null || serverCommand) return;
        serverCommand = true;
        try
        {
            ServerConnectionWindow dialog = new(controller) { Owner = this };
            if (dialog.ShowDialog() == true) ServerStatusText.Text = "Сервер подключён. Настройки сохранены. Можно получить задание на сбор.";
        }
        finally { serverCommand = false; }
    }
    private async void StartServerClick(object sender, RoutedEventArgs e) => await ServerActionAsync(async () =>
    {
        if (controller!.Mode != ParserOperatingMode.Server) throw new InvalidOperationException("Переключите режим на «Через Server».");
        await controller.ConnectServerAsync(); await controller.Server!.StartWorkAsync(CancellationToken.None); await RefreshAsync();
    });
    private async void DeliverServerClick(object sender, RoutedEventArgs e) => await ServerActionAsync(async () => { await controller!.ConnectServerAsync(); await controller.Server!.DeliverAsync(CancellationToken.None); });
    private async void RefreshServerWorkspaceClick(object sender, RoutedEventArgs e) => await ServerActionAsync(RefreshServerWorkspaceAsync);
    private async Task RefreshServerWorkspaceAsync()
    {
        await controller!.ConnectServerAsync();
        CollectorWorkspace workspace = await controller.Server!.ReadWorkspaceAsync(CancellationToken.None);
        ServerGroupInput.ItemsSource = workspace.Groups.Where(item => item.Active).ToArray();
        ServerSearchesGrid.ItemsSource = workspace.Searches;
        ServerStatusText.Text = "Серверные группы и поиски обновлены.";
    }
    private async void AddServerGroupClick(object sender, RoutedEventArgs e) => await ServerActionAsync(async () =>
    {
        if (controller!.Mode != ParserOperatingMode.Server) throw new InvalidOperationException("Переключите режим на «Через Server».");
        await controller.ConnectServerAsync();
        await controller.Server!.CreateGroupAsync(new(Guid.CreateVersion7(), ServerGroupName.Text, 100), CancellationToken.None);
        ServerGroupName.Clear(); await RefreshServerWorkspaceAsync();
    });
    private async void AddLinkToServerClick(object sender, RoutedEventArgs e) => await ServerActionAsync(async () =>
    {
        if (controller!.Mode != ParserOperatingMode.Server) throw new InvalidOperationException("Переключите режим на «Через Server».");
        if (LinksGrid.SelectedItem is not SearchLink link) throw new InvalidOperationException("Сначала выберите одну локальную ссылку.");
        CollectorGroupView? group = ServerGroupInput.SelectedItem as CollectorGroupView;
        CollectorScheduleDefinition schedule = ServerSchedule();
        MessageBoxResult confirmation = MessageBox.Show(this,
            $"Добавить на Server только ссылку «{link.Label}»?\n\nЛокальная группа, расписание, история и результаты переданы не будут.",
            "Добавление ссылки на Server", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes) return;
        await controller.ConnectServerAsync();
        await controller.Server!.CreateSearchAsync(new(Guid.CreateVersion7(), link.Label,
            Enum.Parse<ListingSource>(link.Source.ToString()), link.Url, controller.Store.Settings().MaxPages, group?.Id, schedule), CancellationToken.None);
        await RefreshServerWorkspaceAsync();
    });
    private CollectorScheduleDefinition ServerSchedule()
    {
        CollectorScheduleKind kind = (CollectorScheduleKind)ServerScheduleKindInput.SelectedIndex;
        string value = ServerScheduleValueInput.Text.Trim();
        return kind switch
        {
            CollectorScheduleKind.Manual => new(kind),
            CollectorScheduleKind.Interval when int.TryParse(value, out int minutes) => new(kind, IntervalMinutes: minutes),
            CollectorScheduleKind.FixedTimes => new(kind, FixedTimes: value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)),
            _ => throw new ArgumentException("Для интервала укажите количество минут.")
        };
    }
    public WorkspaceWindow() : this(null) { }
    public WorkspaceWindow(WorkspaceController? controller)
    {
        InitializeComponent(); this.controller = controller;
        Loaded += async (_, _) =>
        {
            try
            {
                string root = WorkspaceController.WorkspaceRoot();
                this.controller ??= new WorkspaceController(Path.Combine(root, "local-data", "spike-002.sqlite"), Path.Combine(root, "browser-profiles"));
                ShowSettings(this.controller.Store.Settings());
                ModeInput.SelectedIndex = this.controller.Mode == ParserOperatingMode.Local ? 0 : 1;
                this.controller.Runner.ManualActionRequired += OnNotice;
                if (this.controller.Mode == ParserOperatingMode.Server && this.controller.SavedServerConnection() != null)
                {
                    try
                    {
                        await this.controller.ConnectServerAsync();
                        ServerStatusText.Text = "Сохранённое подключение восстановлено. Ожидаем задания Server.";
                    }
                    catch (Exception exception) when (exception is ServerDeliveryException or InvalidOperationException or HttpRequestException)
                    { ServerStatusText.Text = "Автоподключение пока не выполнено. Проверьте Server или вставьте новый код подключения."; }
                }
                await RefreshAsync(); refresh.Start();
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or Microsoft.Data.Sqlite.SqliteException)
            { StatusText.Text = ex.Message; StartButton.IsEnabled = false; }
        };
        refresh.Tick += async (_, _) =>
        {
            await RefreshAsync(false);
            if (!serverCommand && this.controller is not null) await this.controller.TickLocalScheduleAsync(CancellationToken.None);
            if (!serverCommand && !serverTick && this.controller?.Mode == ParserOperatingMode.Server && this.controller.Server is { } server)
            {
                serverTick = true;
                try { await server.TickAsync(CancellationToken.None); ServerStatusText.Text = server.Status; }
                finally { serverTick = false; }
            }
        };
        Closing += async (_, e) =>
        {
            if (closing) return;
            e.Cancel = true; closing = true; refresh.Stop();
            try { if (this.controller is not null) { this.controller.Runner.ManualActionRequired -= OnNotice; await this.controller.DisposeAsync(); } }
            finally { _ = Dispatcher.InvokeAsync(Close); }
        };
    }
    private void OnNotice(object? sender, CollectionNotice notice) => _ = Dispatcher.InvokeAsync(() =>
    { NoticePanel.Visibility = Visibility.Visible; NoticeText.Text = $"{notice.Source}, {notice.Worker}: {notice.Reason}\n{notice.Link}"; });
    private async Task ActionAsync(Func<Task> action, Action<Exception>? onError = null)
    {
        if (controller is null) return;
        try { await action(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or JsonException or Microsoft.Data.Sqlite.SqliteException or Microsoft.Playwright.PlaywrightException)
        { StatusText.Text = ex.Message; onError?.Invoke(ex); }
    }
    private async Task RefreshAsync(bool all = true)
    {
        if (reading && !all) return;
        while (reading && !closing) await Task.Delay(50);
        if (controller is null || closing) return;
        reading = true;
        try
        {
            var data = await Task.Run(() => (Links: controller.Store.Links(), Jobs: controller.Store.Jobs()));
            if (all)
            {
                LinksGrid.ItemsSource = data.Links; FilterLink.ItemsSource = data.Links;
                LocalGroup[] groups = controller.Store.Groups();
                LocalGroupsGrid.ItemsSource = groups; ScheduleGroupInput.ItemsSource = groups.Where(item => item.Active).ToArray();
                ScheduleLinkInput.ItemsSource = data.Links; LocalSchedulesGrid.ItemsSource = controller.Store.ScheduledLinks();
                await FindAsync();
            }
            string? id = (JobsGrid.SelectedItem as CollectionJob)?.Id ?? selectedJob;
            JobsGrid.ItemsSource = data.Jobs;
            if (id is not null) JobsGrid.SelectedItem = data.Jobs.FirstOrDefault(x => x.Id == id);
            StartButton.IsEnabled = !starting && !controller.Runner.IsRunning && selectionUpdate.IsCompleted;
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex) { StatusText.Text = ex.Message; }
        finally { reading = false; }
    }
    private void LinkFeedback(string message, bool error)
    {
        LinkFeedbackPanel.Visibility = Visibility.Visible;
        LinkFeedbackPanel.Background = error ? System.Windows.Media.Brushes.MistyRose : System.Windows.Media.Brushes.Honeydew;
        LinkFeedbackText.Text = message;
        UrlInput.BorderBrush = error ? System.Windows.Media.Brushes.Firebrick : System.Windows.Media.Brushes.Gray;
    }
    private async void AddClick(object sender, RoutedEventArgs e)
    {
        AddButton.IsEnabled = false;
        try
        {
            await ActionAsync(async () =>
            {
                SourceSite? source = SourceInput.SelectedIndex == 0 ? null : SourceInput.SelectedIndex == 1 ? SourceSite.Avito : SourceSite.Cian;
                string label = LabelInput.Text, url = UrlInput.Text;
                NormalizedSearch normalized = SearchUrls.Normalize(url, source);
                await Task.Run(() => controller!.Store.SaveLink(label, url, true, source, editing));
                editing = null; UrlInput.Clear(); LabelInput.Clear();
                StatusText.Text = "Ссылка сохранена. " + string.Join(" ", normalized.Warnings);
                LinkFeedback(StatusText.Text, false);
                await RefreshAsync();
            }, ex => LinkFeedback("Не удалось сохранить: " + ex.Message, true));
        }
        finally { AddButton.IsEnabled = true; }
    }
    private async void DiagnosticsClick(object sender, RoutedEventArgs e) => await ActionAsync(async () =>
    {
        string? job = DiagnosticJobInput.IsChecked == true ? selectedJob : null;
        DiagnosticText.Text = controller!.Diagnostics.Describe(await Task.Run(() => controller.Diagnostics.Read(job)));
    });
    private async void JsonStartClick(object sender, RoutedEventArgs e) => await ActionAsync(() =>
    {
        controller!.JsonResponses.Start();
        DiagnosticText.Text = controller.JsonResponses.Status;
        StatusText.Text = "Наблюдение JSON включено. Откройте поиск вручную, выделите зону и прокрутите список.";
        return Task.CompletedTask;
    });
    private void JsonStopClick(object sender, RoutedEventArgs e)
    {
        controller?.JsonResponses.Stop();
        StatusText.Text = controller?.JsonResponses.Status ?? "Запись выключена.";
    }
    private async void JsonReadClick(object sender, RoutedEventArgs e) => await ActionAsync(async () =>
    { DiagnosticText.Text = await Task.Run(() => controller!.JsonResponses.Read()); });
    private async void SelectClick(object sender, RoutedEventArgs e) => await ActionAsync(async () =>
    {
        if (sender is not CheckBox { DataContext: SearchLink link } input) return;
        // Read WPF state on its dispatcher before scheduling durable work.
        bool selected = input.IsChecked == true;
        Task previous = selectionUpdate;
        selectionUpdate = SaveSelectionAsync(previous, () => controller!.Store.SelectLink(link.Id, selected));
        await selectionUpdate; await RefreshAsync();
    });
    private async void SelectAllClick(object sender, RoutedEventArgs e) => await SelectAllAsync(true);
    private async void ClearSelectionClick(object sender, RoutedEventArgs e) => await SelectAllAsync(false);
    private Task SelectAllAsync(bool value) => ActionAsync(async () =>
    {
        Task previous = selectionUpdate;
        selectionUpdate = SaveSelectionAsync(previous, () => { foreach (SearchLink link in controller!.Store.Links()) controller.Store.SelectLink(link.Id, value); });
        await selectionUpdate; await RefreshAsync();
    });
    private static async Task SaveSelectionAsync(Task previous, Action save)
    {
        // Serialize rapid clicks; a failed write blocks that start, but a new user choice can recover.
        try { await previous; } catch (Exception ex) when (ex is InvalidOperationException or IOException or Microsoft.Data.Sqlite.SqliteException) { }
        await Task.Run(save);
    }
    private void EditClick(object sender, RoutedEventArgs e)
    {
        if (LinksGrid.SelectedItem is not SearchLink link) return;
        editing = link.Id; LabelInput.Text = link.Label; UrlInput.Text = link.Url; SourceInput.SelectedIndex = link.Source == SourceSite.Avito ? 1 : 2;
    }
    private async void ArchiveClick(object sender, RoutedEventArgs e) => await ActionAsync(async () =>
    { if (LinksGrid.SelectedItem is SearchLink link) { await Task.Run(() => controller!.Store.ArchiveLink(link.Id)); await RefreshAsync(); } });
    private async void EnableClick(object sender, RoutedEventArgs e) => await ActionAsync(async () =>
    { if (LinksGrid.SelectedItem is SearchLink link) { await Task.Run(() => controller!.Store.SaveLink(link.Label, link.Url, link.Selected, link.Source, link.Id, !link.Enabled)); await RefreshAsync(); } });
    private async void OpenClick(object sender, RoutedEventArgs e) => await ActionAsync(async () =>
    {
        if (LinksGrid.SelectedItem is not SearchLink link) { StatusText.Text = "Выберите сохранённую ссылку."; return; }
        if (controller!.Runner.IsRunning) { StatusText.Text = "Во время сбора используйте уже открытые вкладки."; return; }
        // Manual browsing is separate from collection and never performs a read automatically.
        await controller.OpenManualAsync(link);
        StatusText.Text = "Поиск открыт. Сбор начнётся по кнопке «Обработать отмеченные».";
    });
    private void CaptureManualClick(object sender, RoutedEventArgs e)
    {
        if (controller?.CurrentManualSearch() is not { } current)
        { StatusText.Text = "Сначала откройте сохранённый поиск и настройте фильтры или область в браузере."; return; }
        UrlInput.Text = current.Url; SourceInput.SelectedIndex = current.Source == SourceSite.Avito ? 1 : 2;
        StatusText.Text = "Текущая ссылка взята из браузера. Проверьте название, затем сохраните её.";
    }
    private async void AddLocalGroupClick(object sender, RoutedEventArgs e) => await ActionAsync(async () =>
    {
        await Task.Run(() => controller!.Store.SaveGroup(LocalGroupName.Text, controller.Store.Groups().Length * 10));
        LocalGroupName.Clear(); await RefreshAsync(); StatusText.Text = "Локальная группа сохранена только на этом компьютере.";
    });
    private async void SaveLocalScheduleClick(object sender, RoutedEventArgs e) => await ActionAsync(async () =>
    {
        if (ScheduleLinkInput.SelectedItem is not SearchLink link) throw new InvalidOperationException("Выберите ссылку.");
        string? groupId = (ScheduleGroupInput.SelectedItem as LocalGroup)?.Id;
        LocalScheduleKind kind = (LocalScheduleKind)ScheduleKindInput.SelectedIndex;
        string value = ScheduleValueInput.Text.Trim();
        LocalSchedule schedule = kind switch
        {
            LocalScheduleKind.Manual => new(kind),
            LocalScheduleKind.Interval when int.TryParse(value, out int minutes) => new(kind, IntervalMinutes: minutes),
            LocalScheduleKind.FixedTimes => new(kind, FixedTimes: value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)),
            _ => throw new ArgumentException("Для интервала укажите количество минут.")
        };
        await Task.Run(() => controller!.Store.SaveSchedule(link.Id, groupId, schedule));
        await RefreshAsync(); StatusText.Text = "Локальное расписание сохранено. Оно работает только в режиме «Локально».";
    });
    private void ModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (controller == null || ModeInput.SelectedIndex < 0) return;
        ParserOperatingMode requested = ModeInput.SelectedIndex == 0 ? ParserOperatingMode.Local : ParserOperatingMode.Server;
        try
        {
            controller.SetMode(requested);
            StatusText.Text = requested == ParserOperatingMode.Local
                ? "Локальный режим: группы, расписания и результаты принадлежат этому компьютеру."
                : "Режим Server: задания и расписания принадлежат LandErp Server.";
        }
        catch (InvalidOperationException exception)
        {
            ModeInput.SelectedIndex = controller.Mode == ParserOperatingMode.Local ? 0 : 1;
            StatusText.Text = exception.Message;
        }
    }
    private async void StartClick(object sender, RoutedEventArgs e) => await ActionAsync(async () =>
    {
        if (controller!.Mode != ParserOperatingMode.Local) throw new InvalidOperationException("Переключите режим на «Локально».");
        if (starting || controller!.Runner.IsRunning) return;
        starting = true; StartButton.IsEnabled = false;
        try
        {
            await selectionUpdate;
            await controller.CloseManualAsync();
            CollectionSettings settings = controller.Store.Settings();
            bool force = ForceInput.IsChecked == true;
            NoticePanel.Visibility = Visibility.Collapsed; StatusText.Text = "Сбор выполняется. Браузеры откроются для выбранных источников.";
            await Task.Run(() => controller.Runner.StartAsync(settings, force));
            StatusText.Text = "Очередь завершена. Состояние и причины указаны у каждой задачи.";
        }
        finally { starting = false; await RefreshAsync(); }
    });
    private void PauseClick(object sender, RoutedEventArgs e) { controller?.Runner.Pause(); StatusText.Text = "Пауза. Текущее браузерное действие завершится перед ожиданием."; }
    private void ResumeClick(object sender, RoutedEventArgs e) => controller?.Runner.Resume();
    private void StopClick(object sender, RoutedEventArgs e) { controller?.Runner.Stop(); StatusText.Text = "Остановка; завершённые страницы и частичные результаты сохранены."; }
    private async void ResumeAvitoClick(object sender, RoutedEventArgs e) => await ActionAsync(() => controller!.Runner.ResumeSourceAsync(SourceSite.Avito));
    private async void ResumeCianClick(object sender, RoutedEventArgs e) => await ActionAsync(() => controller!.Runner.ResumeSourceAsync(SourceSite.Cian));
    private async void SaveSettingsClick(object sender, RoutedEventArgs e) => await ActionAsync(async () =>
    {
        SettingsGrid.CommitEdit(DataGridEditingUnit.Cell, true); SettingsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        CollectionSettings settings = SettingsEditor.Read((SettingEntry[])SettingsGrid.ItemsSource, BrowserInput.SelectedIndex == 1 ? "msedge" : "chrome", ErrorInput.SelectedIndex == 1 ? ErrorPolicy.PauseSource : ErrorPolicy.Continue);
        await Task.Run(() => controller!.Store.SaveSettings(settings)); StatusText.Text = "Настройки сохранены для следующего запуска.";
    });
    private void ShowSettings(CollectionSettings settings)
    { SettingsGrid.ItemsSource = SettingsEditor.Entries(settings); BrowserInput.SelectedIndex = settings.Browser == "chrome" ? 0 : 1; ErrorInput.SelectedIndex = settings.ErrorPolicy == ErrorPolicy.Continue ? 0 : 1; }
    private void DefaultsClick(object sender, RoutedEventArgs e) => ShowSettings(new CollectionSettings());
    private async void FindClick(object sender, RoutedEventArgs e) => await ActionAsync(async () => { offset = 0; await FindAsync(); });
    private async Task FindAsync()
    {
        if (controller is null) return;
        SourceSite? source = FilterSource.SelectedIndex == 0 ? null : FilterSource.SelectedIndex == 1 ? SourceSite.Avito : SourceSite.Cian;
        ListingFilter filter = new(SearchInput.Text, source, (FilterLink.SelectedItem as SearchLink)?.Id,
            FilterJob.IsChecked == true ? selectedJob : null, offset, 100, PriceSort.IsChecked == true);
        ListingPage page = await Task.Run(() => controller.Store.ReadListings(filter));
        ListingsGrid.ItemsSource = page.Rows; PageText.Text = $"Всего {page.Total}; показано {offset + (page.Rows.Length == 0 ? 0 : 1)}–{offset + page.Rows.Length}";
    }
    private async void PreviousClick(object sender, RoutedEventArgs e) => await ActionAsync(async () => { offset = Math.Max(0, offset - 100); await FindAsync(); });
    private async void NextClick(object sender, RoutedEventArgs e) => await ActionAsync(async () => { offset += 100; await FindAsync(); });
    private async void ResetFiltersClick(object sender, RoutedEventArgs e) => await ActionAsync(async () =>
    { SearchInput.Clear(); FilterSource.SelectedIndex = 0; FilterLink.SelectedItem = null; FilterJob.IsChecked = false; PriceSort.IsChecked = false; offset = 0; await FindAsync(); });
    private async void ListingSelected(object sender, SelectionChangedEventArgs e) => await ActionAsync(async () => { historyOffset = 0; await DetailsAsync(); });
    private async void MoreHistoryClick(object sender, RoutedEventArgs e) => await ActionAsync(async () => { historyOffset += 100; await DetailsAsync(); });
    private async Task DetailsAsync()
    {
        if (ListingsGrid.SelectedItem is not ListingRow row || controller is null) return;
        HistoryRow[] history = await Task.Run(() => controller.Store.History(row.Source, row.ExternalId, historyOffset));
        DetailsText.Text = "Текущее известное состояние (отсутствие поля не очищает прежнее значение):\n" + LocalJson.Write(row.Observation)
            + "\n\nИсходные наблюдения, включая историю цены (по 100):\n" + LocalJson.Write(history);
    }
    private async void JobSelected(object sender, SelectionChangedEventArgs e) => await ActionAsync(async () =>
    {
        if (JobsGrid.SelectedItem is CollectionJob job) { selectedJob = job.Id; JournalGrid.ItemsSource = await Task.Run(() => controller!.Store.Journal(job.Id)); }
    });
    private async void ExportClick(object sender, RoutedEventArgs e) => await ActionAsync(async () =>
    {
        if (selectedJob is null) { StatusText.Text = "Выберите задачу в очереди."; return; }
        SaveFileDialog dialog = new() { Filter = "JSON|*.json", FileName = "collection-" + selectedJob + ".json" };
        if (dialog.ShowDialog() == true) { await Task.Run(() => { using FileStream stream = File.Create(dialog.FileName); controller!.Store.ExportJob(selectedJob, stream); }); StatusText.Text = "Исходные наблюдения экспортированы."; }
    });
}
