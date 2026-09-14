using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using LandErp.ParserSpike.Avito;
using LandErp.ParserSpike.Application;
using LandErp.ParserSpike.Browser;
using LandErp.ParserSpike.Contracts;
using LandErp.ParserSpike.Serialization;
using LandErp.ParserSpike.Storage;
using System.Text;
using System.Windows.Controls;
using Microsoft.Data.Sqlite;
using Microsoft.Playwright;
using Microsoft.Win32;

namespace LandErp.ParserSpike.Desktop;

public partial class MainWindow : Window, IAsyncDisposable
{
    private readonly AvitoBrowser browser;
    private SearchParseResult? result;
    private ListingStore? store;
    private SearchCollector? collector;
    private CancellationTokenSource? cancellation;
    private readonly string databasePath;
    private bool busy;
    private bool closing;

    public MainWindow() : this(null) { }

    public MainWindow(string? databasePath, AvitoBrowser? researchBrowser = null)
    {
        browser = researchBrowser ?? new AvitoBrowser();
        this.databasePath = databasePath ?? Path.Combine(WorkspaceRoot(), "local-data", "spike-002.sqlite");
        InitializeComponent();
        Loaded += (_, _) => { RefreshDatabaseSafely(); UpdateButtons(); };
        Closing += async (_, e) =>
        {
            if (closing) return;
            e.Cancel = true;
            if (busy) { StatusText.Text = "Дождитесь завершения текущего действия."; return; }
            if (collector?.CanResume == true) collector.Stop();
            closing = true;
            try { await DisposeAsync(); }
            catch (PlaywrightException) { /* Browser may already have been closed manually. */ }
            _ = Dispatcher.InvokeAsync(Close);
        };
    }

    private async void OpenClick(object sender, RoutedEventArgs e)
    {
        if (!Uri.TryCreate(UrlInput.Text.Trim(), UriKind.Absolute, out Uri? url) || !SearchParser.IsAvitoUrl(url))
        { StatusText.Text = "Введите HTTPS-ссылку avito.ru без логина, пароля и фрагмента."; return; }
        await ActionAsync(async () =>
        {
            ClearResult();
            collector = null;
            NoticePanel.Visibility = Visibility.Collapsed;
            StatusText.Text = "Открываю страницу в браузере…";
            await browser.OpenAsync(url, BrowserChoice.SelectedIndex == 0 ? "chrome" : "msedge", ProfileRoot());
            StatusText.Text = "Страница открыта. Дождитесь загрузки, при необходимости войдите или пройдите CAPTCHA. Затем нажмите «Спарсить».";
        });
    }

    private async void ParseClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PageCountInput.Text, out int limit) || limit is < 1 or > 10)
        { StatusText.Text = "Укажите количество страниц от 1 до 10."; return; }
        await ActionAsync(async () =>
        {
            ClearResult();
            store ??= new ListingStore(databasePath);
            collector = new SearchCollector(browser, store, limit);
            await CollectAsync();
        });
    }

    private async void ContinueClick(object sender, RoutedEventArgs e)
    {
        if (collector?.CanResume != true) return;
        await ActionAsync(CollectAsync);
    }

    private void StopClick(object sender, RoutedEventArgs e)
    {
        cancellation?.Cancel();
        if (cancellation is null && collector?.CanResume == true)
        {
            collector.Stop();
            NoticePanel.Visibility = Visibility.Collapsed;
            ShowCollectionNotice();
            RefreshDatabaseSafely();
            UpdateButtons();
            return;
        }
        StatusText.Text = "Останавливаю сбор. Уже сохранённые объявления останутся в базе.";
    }

    private async Task CollectAsync()
    {
        NoticePanel.Visibility = Visibility.Collapsed;
        using CancellationTokenSource tokenSource = new();
        cancellation = tokenSource;
        UpdateButtons();
        try
        {
            await collector!.CollectAsync(new Progress<CollectionProgress>(progress =>
            {
                StatusText.Text = $"Страница {progress.Page} из {progress.Limit} · объявлений: {progress.Count} · {StateLabel(progress.State)}";
                ShowResults();
            }), tokenSource.Token);
        }
        finally
        {
            cancellation = null;
            ShowResults();
            RefreshDatabaseSafely();
            ShowCollectionNotice();
        }
    }

    private void ShowResults()
    {
        if (collector?.LastResult is null) return;
        result = new(collector.LastResult.Metadata, collector.Listings);
        ResultsGrid.ItemsSource = result.Listings.Select(item => new
        {
            item.ExternalId,
            item.Title,
            item.Location,
            item.DateText,
            item.Url,
            item.SellerName,
            item.Badges,
            item.PreviewDescription,
            UnitPriceText = item.PricePerSotka.Raw ?? "Отсутствует",
            PriceText = item.Price.Raw ?? "Отсутствует",
            AreaText = item.AreaSquareMeters.Parsed is decimal area
                ? area.ToString("0.##", CultureInfo.CurrentCulture) + " м²" : "Отсутствует",
            WarningText = string.Join(", ", item.Warnings.Select(WarningLabel))
        }).ToArray();
    }

    private void ShowCollectionNotice()
    {
        if (collector is null) return;
        StatusText.Text = $"Страница {collector.PageNumber} из {collector.Limit} · объявлений: {collector.Listings.Length} · {StateLabel(collector.State)}";
        NoticeText.Text = collector.State switch
        {
            "Captcha" => "Сбор приостановлен. Пройдите проверку в браузере, затем нажмите «Продолжить».",
            "AuthenticationRequired" => "Сбор приостановлен. Войдите вручную в браузере, затем нажмите «Продолжить».",
            "RateLimited" => "Источник ограничил доступ. Подождите и проверьте страницу вручную. Автоматических повторов нет.",
            "Unknown" or "SourceError" => "Сбор приостановлен: выдача не распознана или источник вернул ошибку. Проверьте страницу перед продолжением.",
            "SCROLL_LIMIT_INCOMPLETE" => "Достигнут предел прокрутки. Сбор неполный; уже найденные объявления сохранены.",
            "ListingDetails" => "Это отдельное объявление. Для сбора нужна поисковая выдача.",
            "INVALID_NEXT_PAGE" => "Переход остановлен: ссылка следующей страницы недопустима или уже посещена.",
            "ERROR" => "Сбор завершился ошибкой. Уже сохранённые данные доступны в локальной базе.",
            _ => ""
        };
        NoticePanel.Visibility = NoticeText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void SaveClick(object sender, RoutedEventArgs e)
    {
        if (result is null || busy) return;
        SaveFileDialog dialog = new() { Filter = "JSON (*.json)|*.json", FileName = "avito-result.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            await File.WriteAllTextAsync(dialog.FileName, SpikeJson.Serialize(new
            {
                collector?.RunId,
                State = collector?.State,
                Page = collector?.PageNumber,
                PageLimit = collector?.Limit,
                collector?.QualityErrors,
                Result = result
            }));
            StatusText.Text = "JSON сохранён.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { StatusText.Text = "Не удалось сохранить JSON: проверьте доступ к выбранной папке."; }
    }

    private async Task ActionAsync(Func<Task> action)
    {
        if (busy) return;
        busy = true;
        UpdateButtons();
        try { await action(); }
        catch (System.TimeoutException)
        { StatusText.Text = "Истекло время ожидания. Проверьте страницу в браузере; парсинг можно запустить вручную."; }
        catch (PlaywrightException)
        { StatusText.Text = "Ошибка браузера. Убедитесь, что выбранный Chrome/Edge установлен и профиль не занят. Автоповтора нет."; }
        catch (JsonException)
        { StatusText.Text = "Не удалось прочитать структуру страницы. Успешный результат не получен."; }
        catch (InvalidOperationException ex)
        { StatusText.Text = ex.Message == "NEXT_PAGE_CHANGED" ? "Не удалось подтвердить переход на следующую страницу. Данные сохранены." : "Откройте страницу Avito в исследовательском браузере."; }
        catch (SqliteException)
        { StatusText.Text = "Ошибка локальной базы. Проверьте доступ к файлу или занят ли он другим приложением."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { StatusText.Text = "Нет доступа к локальному профилю или базе данных."; }
        finally { busy = false; UpdateButtons(); }
    }

    private void ClearResult() { result = null; ResultsGrid.ItemsSource = null; }

    /// <summary>Releases the dedicated browser when this window closes.</summary>
    public async ValueTask DisposeAsync()
    {
        await browser.DisposeAsync();
        GC.SuppressFinalize(this);
    }
    private void UpdateButtons()
    {
        UrlInput.IsEnabled = !busy;
        PageCountInput.IsEnabled = !busy && collector?.CanResume != true;
        BrowserChoice.IsEnabled = !busy && !browser.IsOpen;
        OpenButton.IsEnabled = !busy && collector?.CanResume != true;
        ParseButton.IsEnabled = !busy && browser.IsOpen && collector?.CanResume != true;
        ContinueButton.IsEnabled = !busy && browser.IsOpen && collector?.CanResume == true;
        StopButton.IsEnabled = cancellation is not null || (!busy && collector?.CanResume == true);
        SaveButton.IsEnabled = !busy && result is not null;
    }

    private void RefreshClick(object sender, RoutedEventArgs e) => RefreshDatabaseSafely();
    private void SearchChanged(object sender, TextChangedEventArgs e)
    { if (store is not null) RefreshDatabaseSafely(); }

    private void RefreshDatabaseSafely()
    {
        try
        {
            store ??= new ListingStore(databasePath);
            string? selected = (SavedGrid.SelectedItem as SavedListing)?.ExternalId;
            SavedListing[] listings = store.ReadListings(DatabaseSearch.Text.Trim());
            SavedGrid.ItemsSource = listings;
            SavedGrid.SelectedItem = listings.FirstOrDefault(item => item.ExternalId == selected);
            RunsGrid.ItemsSource = store.ReadRuns().Select(run => new
            { run.Started, run.Limit, run.Page, run.Count, run.Url, StateLabel = StateLabel(run.State) }).ToArray();
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or JsonException)
        { StatusText.Text = "Не удалось прочитать локальную базу. Проверьте доступ к файлу."; }
    }

    private void SavedSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ListingDetails is null || store is null) return;
        if (SavedGrid.SelectedItem is not SavedListing saved)
        { ListingDetails.Text = "Выберите объявление: здесь появятся поля и история наблюдений."; return; }
        try
        {
            SearchListing item = saved.Listing;
            StringBuilder text = new();
            text.AppendLine(item.Title).AppendLine(item.Url)
                .AppendLine(CultureInfo.CurrentCulture, $"Впервые: {saved.FirstSeen.ToLocalTime():dd.MM.yyyy HH:mm}")
                .AppendLine(CultureInfo.CurrentCulture, $"Последний сбор: {saved.LastSeen.ToLocalTime():dd.MM.yyyy HH:mm}")
                .AppendLine("\nАктуальные известные значения (могут быть из разных наблюдений):");
            AppendFields(text, item);
            text.AppendLine("\nИстория наблюдений, новые сверху:");
            foreach (SavedObservation observation in store.ReadHistory(item.ExternalId))
            {
                text.AppendLine(CultureInfo.CurrentCulture, $"\n{observation.ObservedAt} · страница {observation.Page} · запуск {observation.RunId}");
                AppendFields(text, observation.Listing);
            }
            ListingDetails.Text = text.ToString();
        }
        catch (Exception ex) when (ex is SqliteException or JsonException)
        { ListingDetails.Text = "Не удалось прочитать историю объявления."; }
    }

    private static void AppendFields(StringBuilder text, SearchListing item)
    {
        text.AppendLine(CultureInfo.CurrentCulture, $"Цена: {Field(item.Price)}")
            .AppendLine(CultureInfo.CurrentCulture, $"Площадь, м²: {Field(item.AreaSquareMeters)}")
            .AppendLine(CultureInfo.CurrentCulture, $"Цена за сотку: {Field(item.PricePerSotka)}")
            .AppendLine(CultureInfo.CurrentCulture, $"Место: {TextField(item.Location)}")
            .AppendLine(CultureInfo.CurrentCulture, $"Дата на сайте: {TextField(item.DateText)}")
            .AppendLine(CultureInfo.CurrentCulture, $"Описание: {TextField(item.PreviewDescription)}")
            .AppendLine(CultureInfo.CurrentCulture, $"Продавец: {TextField(item.SellerName)}")
            .AppendLine(CultureInfo.CurrentCulture, $"Информация продавца: {TextField(item.SellerInfo)}")
            .AppendLine(CultureInfo.CurrentCulture, $"Отметки: {TextField(item.Badges)}")
            .AppendLine(CultureInfo.CurrentCulture, $"Превью: {TextField(item.PhotoUrl)}")
            .AppendLine(CultureInfo.CurrentCulture, $"Предупреждения наблюдения: {string.Join(", ", item.Warnings.Select(WarningLabel))}");
        static string Field(ObservedValue<decimal> value) =>
            $"исходное «{value.Raw ?? "—"}»; значение {value.Parsed?.ToString(CultureInfo.CurrentCulture) ?? "—"}; {value.Presence}" +
            (value.Warnings.Count > 0 ? "; " + string.Join(", ", value.Warnings.Select(WarningLabel)) : "");
        static string TextField(string value) => string.IsNullOrWhiteSpace(value) ? "Отсутствует" : value;
    }

    private static string StateLabel(string state) => state switch
    {
        "READY" => "Подготовлен",
        "RUNNING" => "Сбор выполняется",
        "COMPLETED" => "Заданный объём собран",
        "COMPLETED_WITH_WARNINGS" => "Собрано с ошибками отдельных карточек",
        "NO_VALID_ITEMS" => "Нет валидных объявлений",
        "END_OF_RESULTS" => "Следующей страницы нет",
        "STOPPED" => "Остановлен пользователем",
        "Captcha" => "Требуется проверка",
        "AuthenticationRequired" => "Требуется вход",
        "RateLimited" => "Доступ ограничен",
        "Unknown" => "Выдача не распознана",
        "SourceError" => "Ошибка источника",
        "ListingDetails" => "Открыта карточка вместо выдачи",
        "ERROR" => "Ошибка сбора",
        "INVALID_NEXT_PAGE" => "Некорректная следующая страница",
        "SCROLL_LIMIT_INCOMPLETE" => "Сбор неполный: предел прокрутки",
        "INTERRUPTED" => "Прерван закрытием приложения",
        _ => state
    };

    private static string WarningLabel(string code) => code switch
    {
        "TITLE_ABSENT" => "Нет заголовка",
        "LOCATION_ABSENT" => "Нет местоположения",
        "DATE_ABSENT" => "Нет даты",
        "PRICE_ABSENT" => "Нет цены",
        "PRICE_PARSE_FAILED" => "Цена не распознана",
        "AREA_ABSENT" => "Нет площади в заголовке",
        "AREA_PARSE_FAILED" => "Площадь не распознана",
        "UNIT_PRICE_ABSENT" => "Нет цены за сотку",
        "UNIT_PRICE_PARSE_FAILED" => "Цена за сотку не распознана",
        _ => code
    };

    private static string ProfileRoot()
        => Path.Combine(WorkspaceRoot(), "browser-profiles", "spike-002");

    private static string WorkspaceRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LandErp.slnx"))) directory = directory.Parent;
        return directory?.FullName
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LandErp", "ParserSpike");
    }
}
