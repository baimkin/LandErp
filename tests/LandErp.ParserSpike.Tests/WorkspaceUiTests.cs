using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LandErp.ParserSpike.Desktop;
using LandErp.ParserSpike.LocalCollection;
using LandErp.ParserSpike.ServerIntegration;
using LandErp.Collector.Contracts.V1;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.ParserSpike.Tests;

[TestClass]
[DoNotParallelize]
public sealed class WorkspaceUiTests
{
    private sealed class NoSessions : ISourceSessions
    {
        public int Created;
        public Task<ISourcePage> CreatePageAsync(SourceSite source, CollectionSettings settings, CancellationToken cancellationToken)
        { Created++; throw new InvalidOperationException("NO_BROWSER_EXPECTED"); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    [TestMethod]
    public async Task WorkspaceHasFourSectionsValidatesSettingsAndAddsSearchWithSchedule()
    {
        string root = Path.Combine(Path.GetTempPath(), "LandErp-WorkspaceUi", Guid.NewGuid().ToString("N"));
        NoSessions sessions = new();
        WorkspaceController controller = new(Path.Combine(root, "data.sqlite"), root, sessions);
        await OnSta(async () =>
        {
            WorkspaceWindow window = new(controller); window.Show();
            try
            {
                await Until(() => ((TextBox)window.FindName("MaxPagesInput")).Text.Length > 0);
                TabControl tabs = (TabControl)window.FindName("Pages");
                Assert.AreEqual(4, tabs.Items.Count);
                CollectionAssert.AreEqual((string[])["Поиски", "Работа", "Результаты", "Настройки"], tabs.Items.Cast<TabItem>().Select(x => (string)x.Header).ToArray());
                Assert.IsNull(window.FindName("SettingsGrid")); Assert.IsNull(window.FindName("DiagnosticText"));
                Assert.AreEqual(0, sessions.Created); Assert.IsFalse(controller.AutomationEnabled);
                tabs.SelectedIndex = 3;
                ((TextBox)window.FindName("MaxPagesInput")).Text = "0";
                ((Button)window.FindName("SaveSettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.AreEqual(10, controller.Store.Settings().MaxPages);
                ((TextBox)window.FindName("MaxPagesInput")).Text = "7";
                ((Button)window.FindName("SaveSettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Until(() => controller.Store.Settings().MaxPages == 7);
                tabs.SelectedIndex = 0;
                _ = window.Dispatcher.BeginInvoke(() =>
                {
                    SearchEditorWindow editor = window.OwnedWindows.OfType<SearchEditorWindow>().Single();
                    ((TextBox)editor.FindName("LabelInput")).Text = "Участки в Подмосковье";
                    ((TextBox)editor.FindName("UrlInput")).Text = "https://www.avito.ru/pushkino/zemelnye_uchastki";
                    ((ComboBox)editor.FindName("ScheduleKindInput")).SelectedIndex = 1;
                    ((TextBox)editor.FindName("ScheduleValueInput")).Text = "60";
                    ((Button)editor.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }, DispatcherPriority.ApplicationIdle);
                ((Button)window.FindName("AddButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Until(() => controller.Store.Links().Length == 1);
                Assert.AreEqual(60, controller.Store.ScheduledLinks().Single().Schedule.IntervalMinutes);
                Assert.AreEqual(1, ((DataGrid)window.FindName("LinksGrid")).Items.Count);
                window.UpdateLayout(); double small = ((DataGrid)window.FindName("LinksGrid")).ActualWidth;
                window.Width = 1600; window.Height = 1000; window.UpdateLayout();
                Assert.IsTrue(((DataGrid)window.FindName("LinksGrid")).ActualWidth > small);
                string? image = Environment.GetEnvironmentVariable("LANDERP_LOCAL_UI_SCREENSHOT");
                if (image != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(image)!);
                    for (int i = 0; i < 4; i++)
                    {
                        tabs.SelectedIndex = i; window.UpdateLayout();
                        RenderTargetBitmap bitmap = new((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                        bitmap.Render(window); PngBitmapEncoder encoder = new(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using FileStream stream = File.Create(Path.ChangeExtension(image, i + ".png")); encoder.Save(stream);
                    }
                }
                Assert.AreEqual(0, sessions.Created); Assert.AreEqual(0, controller.Store.Jobs().Length);
            }
            finally { window.Close(); await Until(() => !window.IsVisible); }
        });
    }
    [TestMethod]
    public async Task ServerEditorCreatesRemoteSearchWithoutStagingLocalData()
    {
        string root = Path.Combine(Path.GetTempPath(), "LandErp-ServerUi", Guid.NewGuid().ToString("N"));
        using WorkspaceServer handler = new(); NoSessions sessions = new();
        WorkspaceController controller = new(Path.Combine(root, "data.sqlite"), root, sessions, () => new HttpClient(handler, false));
        controller.Store.SaveLink("Личный проект", "https://www.avito.ru/moskva/zemelnye_uchastki");
        await controller.ConnectServerAsync(new(new Uri("https://server.test/"), Guid.CreateVersion7(), new string('A', 64)));
        controller.SetMode(ParserOperatingMode.Server);
        await OnSta(async () =>
        {
            WorkspaceWindow window = new(controller); window.Show();
            try
            {
                DataGrid grid = (DataGrid)window.FindName("LinksGrid");
                await Until(() => ((ListBox)window.FindName("GroupsList")).Items.Count == 3);
                Assert.AreEqual(0, grid.Items.Count);
                Assert.AreEqual(Visibility.Collapsed, ((Button)window.FindName("TransferButton")).Visibility);
                _ = window.Dispatcher.BeginInvoke(() =>
                {
                    SearchEditorWindow editor = window.OwnedWindows.OfType<SearchEditorWindow>().Single();
                    ((TextBox)editor.FindName("LabelInput")).Text = "Серверный поиск";
                    ((TextBox)editor.FindName("UrlInput")).Text = "https://www.avito.ru/korolev/zemelnye_uchastki";
                    ((ComboBox)editor.FindName("GroupInput")).SelectedIndex = 1;
                    ((Button)editor.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }, DispatcherPriority.ApplicationIdle);
                ((Button)window.FindName("AddButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Until(() => grid.Items.Count == 1);
                Assert.AreEqual("Серверный поиск", ((WorkspaceSearch)grid.Items[0]).Label);
                Assert.AreEqual("Личный проект", controller.Store.Links().Single().Label);
                Assert.AreEqual(0, controller.Store.Groups().Length); Assert.AreEqual(0, sessions.Created);
                Assert.AreEqual(handler.Group.Id, handler.Searches.Single().GroupId);
            }
            finally { window.Close(); await Until(() => !window.IsVisible); }
        });
    }
    private sealed class WorkspaceServer : HttpMessageHandler
    {
        public CollectorGroupView Group { get; } = new(Guid.CreateVersion7(), "Серверная группа", 10, true, 1);
        public List<CollectorSearchView> Searches { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            object response;
            if (request.RequestUri!.AbsolutePath.EndsWith("/workspace", StringComparison.Ordinal)) response = new CollectorWorkspace([Group], Searches.ToArray(), [CollectorFeatures.SearchManage]);
            else if (request.RequestUri.AbsolutePath.EndsWith("/workspace/searches", StringComparison.Ordinal))
            {
                CreateCollectorSearch command = JsonSerializer.Deserialize<CreateCollectorSearch>(await request.Content!.ReadAsStringAsync(cancellationToken), CollectionJson.Options)!;
                CollectorSearchView search = new(command.CommandId, command.Label, command.Source, command.Url, command.MaxPages, command.GroupId, command.Schedule, true, 1);
                Searches.Add(search); response = search;
            }
            else response = new { contractVersion = 1 };
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(response, options: CollectionJson.Options) };
        }
    }

    [TestMethod]
    public void CsvQuotesTextAndPreventsSpreadsheetFormulaExecution()
    {
        Assert.AreEqual("\"'=SUM(A1:A2)\"", WorkspaceWindow.CsvCell("=SUM(A1:A2)"));
        Assert.AreEqual("\"слово;\"\"цитата\"\"\"", WorkspaceWindow.CsvCell("слово;\"цитата\""));
    }
    [TestMethod]
    public async Task StopSurvivesRestartAndModeSwitchNeverTransfersLocalData()
    {
        string root = Path.Combine(Path.GetTempPath(), "LandErp-StopUi", Guid.NewGuid().ToString("N"));
        string database = Path.Combine(root, "data.sqlite");
        await using (WorkspaceController first = new(database, root, new NoSessions()))
        {
            first.Store.SaveSearch("Другой проект", "https://www.avito.ru/pushkino/zemelnye_uchastki", null, new(LocalScheduleKind.Interval, 5));
            first.SetAutomation(true); first.SetAutomation(false);
        }
        await using WorkspaceController reopened = new(database, root, new NoSessions());
        Assert.IsFalse(reopened.AutomationEnabled);
        await reopened.TickLocalScheduleAsync(CancellationToken.None);
        Assert.AreEqual(0, reopened.Store.Jobs().Length);
        reopened.SetAutomation(true); reopened.SetMode(ParserOperatingMode.Server);
        Assert.IsFalse(reopened.AutomationEnabled);
        Assert.IsNull(reopened.Server); Assert.AreEqual(1, reopened.Store.Links().Length);
        Assert.IsFalse(File.Exists(Path.Combine(root, "server-connection.json")));
    }
    private static async Task Until(Func<bool> predicate)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (!predicate()) { if (DateTimeOffset.UtcNow > deadline) Assert.Fail("UI_TIMEOUT"); await Task.Delay(50); }
    }
    private static Task OnSta(Func<Task> action)
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() =>
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            _ = dispatcher.InvokeAsync(async () =>
            {
                try { await action(); completion.SetResult(); }
                catch (Exception ex) { completion.SetException(ex); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
}
