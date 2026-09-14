using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LandErp.ParserSpike.Desktop;
using LandErp.ParserSpike.LocalCollection;
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
    public async Task WindowAddsSearchesValidatesSettingsSearchesAndResizesWithoutAutomaticCollection()
    {
        string root = Path.Combine(Path.GetTempPath(), "LandErp-WorkspaceUi", Guid.NewGuid().ToString("N"));
        NoSessions sessions = new();
        WorkspaceController controller = new(Path.Combine(root, "data.sqlite"), root, sessions);
        await OnSta(async () =>
        {
            WorkspaceWindow window = new(controller); window.Show();
            await Until(() => ((DataGrid)window.FindName("SettingsGrid")).ItemsSource is not null);
            Assert.AreEqual(0, sessions.Created); Assert.IsFalse(controller.Runner.IsRunning);
            TextBox url = (TextBox)window.FindName("UrlInput");
            Button add = (Button)window.FindName("AddButton");
            url.Text = "https://example.org/search";
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => add.IsEnabled);
            Assert.AreEqual("https://example.org/search", url.Text);
            Assert.AreEqual(Visibility.Visible, ((Border)window.FindName("LinkFeedbackPanel")).Visibility);
            StringAssert.Contains(((TextBlock)window.FindName("LinkFeedbackText")).Text, "Не удалось сохранить");
            Assert.AreEqual(0, controller.Store.Links().Length);
            url.Text = "https://www.avito.ru/pushkino/zemelnye_uchastki?q=земля";
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Until(() => controller.Store.Links().Length == 1);
            await Until(() => ((DataGrid)window.FindName("LinksGrid")).Items.Count == 1);
            url.Text = "https://zvenigorod.cian.ru/kupit-zemelniy-uchastok-moskovskaya-oblast-odincovskiy-gorodskoy-okrug/";
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Until(() => controller.Store.Links().Length == 2);
            await Until(() => ((DataGrid)window.FindName("LinksGrid")).Items.Count == 2);
            SettingEntry[] entries = (SettingEntry[])((DataGrid)window.FindName("SettingsGrid")).ItemsSource;
            entries.Single(x => x.Key == "maxPages").Value = "7";
            ((Button)window.FindName("SaveSettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => controller.Store.Settings().MaxPages == 7);
            window.UpdateLayout(); double small = ((DataGrid)window.FindName("LinksGrid")).ActualWidth;
            window.Width = 1600; window.Height = 1000; window.UpdateLayout();
            Assert.IsTrue(((DataGrid)window.FindName("LinksGrid")).ActualWidth > small);
            string? image = Environment.GetEnvironmentVariable("LANDERP_LOCAL_UI_SCREENSHOT");
            if (image is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(image)!);
                RenderTargetBitmap bitmap = new((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(window); PngBitmapEncoder encoder = new(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using FileStream stream = File.Create(image); encoder.Save(stream);
            }
            Assert.AreEqual(0, sessions.Created); Assert.AreEqual(0, controller.Store.Jobs().Length);
            window.Close(); await Until(() => !window.IsVisible);
        });
    }
    [TestMethod]
    public async Task CheckboxSelectionIsPersistedBeforeImmediateStart()
    {
        string root=Path.Combine(Path.GetTempPath(),"LandErp-SelectionUi",Guid.NewGuid().ToString("N"));
        NoSessions sessions=new();
        WorkspaceController controller=new(Path.Combine(root,"data.sqlite"),root,sessions);
        controller.Store.SaveLink("Avito","https://www.avito.ru/korolev/zemelnye_uchastki");
        controller.Store.SaveLink("Cian","https://www.cian.ru/cat.php?region=1");
        await OnSta(async()=>
        {
            WorkspaceWindow window=new(controller);window.Show();
            DataGrid grid=(DataGrid)window.FindName("LinksGrid");
            await Until(()=>grid.Items.Count==2);window.UpdateLayout();
            SearchLink cian=controller.Store.Links().Single(x=>x.Source==SourceSite.Cian);
            DataGridRow row=(DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(grid.Items.Cast<SearchLink>().Single(x=>x.Id==cian.Id));
            CheckBox checkbox=FindCheckbox(row)!;Assert.IsNotNull(checkbox);
            checkbox.IsChecked=false;checkbox.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ((Button)window.FindName("StartButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(()=>controller.Store.Jobs().Length>0);
            Assert.IsFalse(controller.Store.Links().Single(x=>x.Id==cian.Id).Selected);
            Assert.AreEqual(1,controller.Store.Jobs().Length);Assert.AreEqual(SourceSite.Avito,controller.Store.Jobs().Single().Source);
            await Until(()=>!controller.Runner.IsRunning);
            window.Close();await Until(()=>!window.IsVisible);
        });
        static CheckBox? FindCheckbox(DependencyObject parent)
        {
            if(parent is CheckBox box)return box;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
            {CheckBox? found=FindCheckbox(VisualTreeHelper.GetChild(parent,i));if(found is not null)return found;}
            return null;
        }
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
