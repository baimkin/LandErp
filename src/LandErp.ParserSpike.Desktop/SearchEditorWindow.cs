using System.Windows;
using System.Windows.Controls;
using LandErp.ParserSpike.LocalCollection;
using LandErp.Collector.Contracts.V1;

namespace LandErp.ParserSpike.Desktop;

public sealed record WorkspaceGroup(string? Id, string Name);
public sealed record SearchDraft(string Label, string Url, string? GroupId, LocalSchedule Schedule, int MaxPages)
{
    public CollectorScheduleDefinition ServerSchedule => new((CollectorScheduleKind)Schedule.Kind, Schedule.IntervalMinutes, Schedule.FixedTimes);
}

/// <summary>The same editor serves both modes; the caller chooses exactly one destination.</summary>
public sealed class SearchEditorWindow : Window
{
    private readonly TextBox label = new(), url = new(), value = new(), pages = new();
    private readonly ComboBox group = new() { DisplayMemberPath = "Name" }, kind = new();
    private readonly TextBlock error = new() { Foreground = System.Windows.Media.Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
    private bool saving;
    public SearchEditorWindow(string title, SearchDraft initial, WorkspaceGroup[] groups, bool server, bool requireGroup, Func<SearchDraft, Task> save)
    {
        Title = title; Width = 610; SizeToContent = SizeToContent.Height; MaxHeight = 850;
        NameScope.SetNameScope(this, new NameScope());
        RegisterName("LabelInput", label); RegisterName("UrlInput", url); RegisterName("GroupInput", group);
        RegisterName("ScheduleKindInput", kind); RegisterName("ScheduleValueInput", value);
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/LandErp.ParserSpike.Desktop;component/ParserTheme.xaml", UriKind.Relative) });
        Style = (Style)FindResource(typeof(Window));
        StackPanel form = new() { Margin = new Thickness(24) };
        Content = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        form.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) });
        Add("Название", label); label.Text = initial.Label;
        Add("Ссылка на выдачу", url); url.Text = initial.Url;
        Add("Группа", group); group.ItemsSource = new[] { new WorkspaceGroup(null, requireGroup ? "Выберите группу сервера" : "Без группы") }.Concat(groups).ToArray();
        group.SelectedIndex = 0;
        if (initial.GroupId != null) group.SelectedItem = ((WorkspaceGroup[])group.ItemsSource).FirstOrDefault(x => x.Id == initial.GroupId) ?? ((WorkspaceGroup[])group.ItemsSource)[0];
        Add("Когда запускать", kind); kind.ItemsSource = new[] { "Вручную", "Через интервал", "В определённое время" }; kind.SelectedIndex = (int)initial.Schedule.Kind;
        TextBlock hint = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) };
        form.Children.Add(hint); form.Children.Add(value);
        value.Text = initial.Schedule.Kind == LocalScheduleKind.Interval ? initial.Schedule.IntervalMinutes?.ToString(System.Globalization.CultureInfo.CurrentCulture) ?? "60" : string.Join(", ", initial.Schedule.FixedTimes ?? []);
        void ScheduleChanged()
        {
            value.Visibility = kind.SelectedIndex == 0 ? Visibility.Collapsed : Visibility.Visible;
            hint.Text = kind.SelectedIndex == 1 ? "Интервал в минутах: от 5 до 10080." : kind.SelectedIndex == 2
                ? "Время через запятую, например 09:00, 18:30. " + (server ? "Часовой пояс организации на сервере." : "Часовой пояс: " + initial.Schedule.TimeZoneId + ".") : "Запуск по кнопке «Запустить выбранный».";
        }
        kind.SelectionChanged += (_, _) => ScheduleChanged(); ScheduleChanged();
        if (server) { Add("Предел страниц (1–100)", pages); pages.Text = initial.MaxPages.ToString(System.Globalization.CultureInfo.CurrentCulture); }
        form.Children.Add(new TextBlock { Text = "После настройки карты убедитесь, что область и фильтры сохранились в ссылке.", TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.SlateGray, Margin = new Thickness(0, 8, 0, 12) });
        form.Children.Add(error);
        StackPanel buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Button cancel = new() { Content = "Отмена", IsCancel = true }, accept = new() { Content = "Сохранить", IsDefault = true };
        RegisterName("SaveButton", accept); RegisterName("ErrorText", error);
        buttons.Children.Add(cancel); buttons.Children.Add(accept); form.Children.Add(buttons);
        Closing += (_, e) => { if (saving) e.Cancel = true; };
        accept.Click += async (_, _) =>
        {
            if (saving) return;
            try
            {
                error.Text = "";
                NormalizedSearch normalized = SearchUrls.Normalize(url.Text.Trim());
                string name = label.Text.Trim();
                if (name.Length is < 1 or > 200 || name.Any(char.IsControl)) throw new ArgumentException("Введите название: от 1 до 200 символов.");
                string? groupId = (group.SelectedItem as WorkspaceGroup)?.Id;
                if (requireGroup && groupId == null) throw new ArgumentException("Выберите группу на сервере.");
                LocalScheduleKind scheduleKind = (LocalScheduleKind)kind.SelectedIndex;
                int? interval = scheduleKind == LocalScheduleKind.Interval && int.TryParse(value.Text, out int minutes) ? minutes : null;
                if (scheduleKind == LocalScheduleKind.Interval && interval is not (>= 5 and <= 10080)) throw new ArgumentException("Интервал должен быть от 5 до 10080 минут.");
                string[] times = scheduleKind == LocalScheduleKind.FixedTimes ? value.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : [];
                LocalSchedule schedule = new(scheduleKind, interval, times, initial.Schedule.TimeZoneId); schedule.Validate();
                int maxPages = initial.MaxPages;
                if (server && (!int.TryParse(pages.Text, out maxPages) || maxPages is < 1 or > 100)) throw new ArgumentException("Предел страниц: от 1 до 100.");
                saving = true; accept.IsEnabled = cancel.IsEnabled = false;
                await save(new(name, normalized.Url, groupId, schedule, maxPages));
                saving = false; DialogResult = true;
            }
            catch (Exception ex) { error.Text = WorkspaceWindow.FriendlyError(ex); }
            finally { saving = false; accept.IsEnabled = cancel.IsEnabled = true; }
        };
        void Add(string caption, Control control) { form.Children.Add(new TextBlock { Text = caption }); form.Children.Add(control); }
    }

    public static string? AskName(Window owner, string title, string initial = "")
    {
        TextBox input = new() { Text = initial, Margin = new Thickness(0, 12, 0, 12) };
        Button save = new() { Content = "Сохранить", IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
        StackPanel panel = new() { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 20 }); panel.Children.Add(input); panel.Children.Add(save);
        Window dialog = new() { Owner = owner, Title = title, Width = 440, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel };
        save.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(input.Text)) dialog.DialogResult = true; };
        return dialog.ShowDialog() == true ? input.Text.Trim() : null;
    }
    public static string? ChooseSource(Window owner)
    {
        string? url = null;
        StackPanel panel = new() { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "Где настроить поиск?", FontSize = 22, Margin = new Thickness(0, 0, 0, 16) });
        Window dialog = new() { Owner = owner, Title = "Открыть сайт", Width = 380, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel };
        foreach (var source in new[] { (Name: "Авито", Url: "https://www.avito.ru/"), (Name: "Циан", Url: "https://www.cian.ru/cat.php?deal_type=sale&engine_version=2&offer_type=suburban&object_type%5B0%5D=3") })
        {
            Button button = new() { Content = source.Name, Margin = new Thickness(0, 4, 0, 4), Padding = new Thickness(16, 12, 16, 12) };
            button.Click += (_, _) => { url = source.Url; dialog.DialogResult = true; }; panel.Children.Add(button);
        }
        dialog.ShowDialog(); return url;
    }
}

