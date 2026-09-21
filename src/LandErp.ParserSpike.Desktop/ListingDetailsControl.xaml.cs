using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using LandErp.ParserSpike.LocalCollection;

namespace LandErp.ParserSpike.Desktop;

public sealed class ListingOpenEventArgs(string url) : EventArgs
{
    public string Url { get; } = url;
}

public partial class ListingDetailsControl : UserControl
{
    private string? currentUrl;
    public string? CurrentExternalId { get; private set; }
    public event EventHandler<ListingOpenEventArgs>? OpenListingRequested;

    public ListingDetailsControl()
    {
        InitializeComponent();
        Clear();
    }

    public void Clear()
    {
        CurrentExternalId = null; currentUrl = null;
        TitleText.Text = "Выберите объявление"; MetaText.Text = FactsText.Text = DeclaredText.Text = InferredText.Text =
            DescriptionText.Text = ContextText.Text = HistoryText.Text = TechnicalText.Text = "";
        ConflictPanel.Visibility = Visibility.Collapsed; PhotoPanel.Visibility = Visibility.Collapsed;
        PhotoPreview.Source = null; PhotoCountText.Text = ""; OpenButton.IsEnabled = false;
    }

    public void ShowListing(ListingRow row, HistoryRow[] history, string context)
    {
        ListingObservation data = row.Observation;
        CurrentExternalId = row.ExternalId; currentUrl = data.Url; OpenButton.IsEnabled = true;
        TitleText.Text = string.IsNullOrWhiteSpace(row.Title) ? "Название не указано" : row.Title;
        MetaText.Text = $"{row.Source} · ID {row.ExternalId}";
        FactsText.Text =
            $"Цена: {row.Price}\nПлощадь: {row.Area}\nЦена за сотку: {row.PricePerSotka}\n" +
            $"Адрес: {(string.IsNullOrWhiteSpace(row.Location) ? "—" : row.Location)}\nПродавец: {(string.IsNullOrWhiteSpace(row.Seller) ? "—" : row.Seller)}\n" +
            $"Опубликовано на источнике: {row.Published}\nКадастровый номер: {row.CadastralNumber}\nКоординаты WGS84: {row.Coordinates}\n" +
            $"Первое наблюдение: {row.FirstSeen.ToLocalTime():dd.MM.yyyy HH:mm}\nПоследнее наблюдение: {row.LastSeen.ToLocalTime():dd.MM.yyyy HH:mm}";
        DeclaredText.Text = "Заявлено источником: " + LandTypeLabels.Format(data.DeclaredLandTypes);
        InferredText.Text = "Найдено в тексте: " + LandTypeLabels.Format(data.InferredLandTypes);
        if (data.LandTypeConflict)
        {
            LandType[] declaredOnly = data.DeclaredLandTypes.Except(data.InferredLandTypes).ToArray();
            LandType[] inferredOnly = data.InferredLandTypes.Except(data.DeclaredLandTypes).ToArray();
            List<string> parts = ["⚠ Данные о типе участка расходятся."];
            if (inferredOnly.Length > 0) parts.Add("Дополнительно найдено в тексте: " + LandTypeLabels.Format(inferredOnly) + ".");
            if (declaredOnly.Length > 0) parts.Add("Заявлено источником, но не найдено в тексте: " + LandTypeLabels.Format(declaredOnly) + ".");
            ConflictText.Text = string.Join(" ", parts); ConflictPanel.Visibility = Visibility.Visible;
        }
        else ConflictPanel.Visibility = Visibility.Collapsed;
        DescriptionText.Text = string.IsNullOrWhiteSpace(data.Description.Raw) ? "—" : data.Description.Raw;
        PhotoCountText.Text = $"Фотографии: {data.PhotoUrls.Length}";
        PhotoPreview.Source = null; PhotoPanel.Visibility = Visibility.Collapsed;
        string? firstPhoto = data.PhotoUrls.FirstOrDefault();
        if (Uri.TryCreate(firstPhoto, UriKind.Absolute, out Uri? photo) && photo.Scheme == Uri.UriSchemeHttps)
        {
            try
            {
                BitmapImage image = new(); image.BeginInit(); image.UriSource = photo; image.CacheOption = BitmapCacheOption.OnDemand; image.EndInit();
                PhotoPreview.Source = image; PhotoPanel.Visibility = Visibility.Visible;
            }
            catch (UriFormatException) { }
            catch (InvalidOperationException) { }
        }
        ContextText.Text = context;
        HistoryText.Text = History(history);
        TechnicalText.Text = Technical(data, history);
    }

    private static string History(HistoryRow[] history)
    {
        if (history.Length == 0) return "Истории пока нет.";
        List<string> rows = [];
        for (int index = 0; index < Math.Min(100, history.Length); index++)
        {
            HistoryRow current = history[index];
            ListingObservation? previous = index + 1 < history.Length ? history[index + 1].Observation : null;
            string changes = previous == null ? "первое сохранённое наблюдение" : Changes(previous, current.Observation);
            rows.Add($"{current.Observation.ObservedAtUtc.ToLocalTime():dd.MM.yyyy HH:mm} · стр. {current.Page} · {changes}");
        }
        return string.Join("\n", rows);
    }

    private static string Changes(ListingObservation previous, ListingObservation current)
    {
        List<string> changes = [];
        static string Money(ListingObservation value) => value.Price.Parsed?.ToString("N0", CultureInfo.GetCultureInfo("ru-RU")) ?? value.Price.Raw ?? "—";
        static string Area(ListingObservation value) => value.AreaSquareMeters.Parsed?.ToString("0.####", CultureInfo.InvariantCulture) ?? "—";
        static string Coord(ListingObservation value) => value.Latitude.Parsed is decimal lat && value.Longitude.Parsed is decimal lon
            ? $"{lat.ToString(CultureInfo.InvariantCulture)}, {lon.ToString(CultureInfo.InvariantCulture)}" : "—";
        static void Add(List<string> target, string label, string? before, string? after)
        { if (!string.Equals(before ?? "", after ?? "", StringComparison.Ordinal)) target.Add($"{label}: {before ?? "—"} → {after ?? "—"}"); }

        Add(changes, "цена", Money(previous), Money(current));
        Add(changes, "площадь, м²", Area(previous), Area(current));
        Add(changes, "адрес", previous.Location.Raw, current.Location.Raw);
        Add(changes, "кадастровый №", previous.CadastralNumber.Raw, current.CadastralNumber.Raw);
        Add(changes, "координаты", Coord(previous), Coord(current));
        Add(changes, "заявлено", LandTypeLabels.Format(previous.DeclaredLandTypes), LandTypeLabels.Format(current.DeclaredLandTypes));
        Add(changes, "найдено в тексте", LandTypeLabels.Format(previous.InferredLandTypes), LandTypeLabels.Format(current.InferredLandTypes));
        Add(changes, "дата источника", previous.SourcePublishedAtUtc?.ToString("O"), current.SourcePublishedAtUtc?.ToString("O"));
        Add(changes, "фото", previous.PhotoUrls.Length.ToString(CultureInfo.InvariantCulture), current.PhotoUrls.Length.ToString(CultureInfo.InvariantCulture));
        if (!string.Equals(previous.Description.Raw ?? "", current.Description.Raw ?? "", StringComparison.Ordinal)) changes.Add("описание изменено");
        if (!string.Equals(previous.SellerName.Raw ?? "", current.SellerName.Raw ?? "", StringComparison.Ordinal))
            changes.Add($"продавец: {previous.SellerName.Raw ?? "—"} → {current.SellerName.Raw ?? "—"}");
        return changes.Count == 0 ? "значимые поля без изменений" : string.Join("; ", changes);
    }

    private static string Technical(ListingObservation data, HistoryRow[] history)
    {
        static string T(string name, TextValue value) => $"{name}: {value.Presence} | raw={value.Raw ?? "<null>"}";
        static string N(string name, NumberValue value) => $"{name}: {value.Presence} | raw={value.Raw ?? "<null>"} | parsed={value.Parsed?.ToString(CultureInfo.InvariantCulture) ?? "<null>"}";
        StringBuilder text = new();
        text.AppendLine(CultureInfo.InvariantCulture, $"schemaVersion: {data.SchemaVersion}");
        text.AppendLine(CultureInfo.InvariantCulture, $"adapterVersion: {data.AdapterVersion}");
        text.AppendLine(CultureInfo.InvariantCulture, $"provenance: {data.Provenance}");
        text.AppendLine(CultureInfo.InvariantCulture, $"source: {data.Source}");
        text.AppendLine(CultureInfo.InvariantCulture, $"externalId: {data.ExternalId}");
        text.AppendLine(CultureInfo.InvariantCulture, $"url: {data.Url}");
        text.AppendLine(CultureInfo.InvariantCulture, $"observedAtUtc: {data.ObservedAtUtc:O}");
        text.AppendLine(CultureInfo.InvariantCulture, $"sourcePublishedAtUtc: {data.SourcePublishedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "<null>"}");
        text.AppendLine(T("title", data.Title));
        text.AppendLine(N("price", data.Price));
        text.AppendLine(N("unitPrice", data.UnitPrice));
        text.AppendLine(N("areaSquareMeters", data.AreaSquareMeters));
        text.AppendLine(T("location", data.Location));
        text.AppendLine(T("transport", data.Transport));
        text.AppendLine(T("description", data.Description));
        text.AppendLine(T("dateText", data.DateText));
        text.AppendLine(T("cadastralNumber", data.CadastralNumber));
        text.AppendLine(N("latitude", data.Latitude));
        text.AppendLine(N("longitude", data.Longitude));
        text.AppendLine("declaredLandTypes: " + string.Join(", ", data.DeclaredLandTypes));
        text.AppendLine("inferredLandTypes: " + string.Join(", ", data.InferredLandTypes));
        text.AppendLine(CultureInfo.InvariantCulture, $"landTypeConflict: {data.LandTypeConflict}");
        text.AppendLine(T("sellerName", data.SellerName));
        text.AppendLine(T("sellerType", data.SellerType));
        text.AppendLine(T("sellerUrl", data.SellerUrl));
        text.AppendLine("warnings: " + (data.Warnings.Length == 0 ? "<none>" : string.Join(", ", data.Warnings)));
        text.AppendLine(CultureInfo.InvariantCulture, $"photos: {data.PhotoUrls.Length}");
        foreach (string photo in data.PhotoUrls) text.AppendLine("  " + photo);
        if (history.Length > 0)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"latestJobId: {history[0].JobId}");
            text.AppendLine(CultureInfo.InvariantCulture, $"latestPage: {history[0].Page}");
        }
        return text.ToString();
    }

    private void OpenClick(object sender, RoutedEventArgs e)
    { if (currentUrl is not null) OpenListingRequested?.Invoke(this, new(currentUrl)); }
}
