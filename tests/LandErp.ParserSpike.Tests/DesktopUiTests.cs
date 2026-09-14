using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LandErp.ParserSpike.Desktop;
using LandErp.ParserSpike.Storage;
using LandErp.ParserSpike.Avito;
using LandErp.ParserSpike.Browser;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.ParserSpike.Tests;

[TestClass]
[DoNotParallelize]
public sealed class DesktopUiTests
{
    [TestMethod]
    public async Task StartupShowsEmptyResultsAndDoesNotEnableParse()
    {
        await OnStaThread(async () =>
        {
            MainWindow window = new(TestDatabasePath());
            window.Show();
            window.UpdateLayout();
            Assert.IsFalse(((Button)window.FindName("ParseButton")).IsEnabled);
            Assert.IsFalse(((Button)window.FindName("SaveButton")).IsEnabled);
            Assert.IsFalse(((Button)window.FindName("ContinueButton")).IsEnabled);
            Assert.IsFalse(((Button)window.FindName("StopButton")).IsEnabled);
            Assert.AreEqual("1", ((TextBox)window.FindName("PageCountInput")).Text);
            Assert.IsNull(((DataGrid)window.FindName("ResultsGrid")).ItemsSource);
            StringAssert.Contains(((TextBlock)window.FindName("StatusText")).Text, "Введите поисковую ссылку");
            string? imagePath = Environment.GetEnvironmentVariable("LANDERP_UI_SCREENSHOT");
            if (!string.IsNullOrEmpty(imagePath))
            {
                RenderTargetBitmap bitmap = new((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(window);
                PngBitmapEncoder encoder = new();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using FileStream stream = File.Create(imagePath);
                encoder.Save(stream);
            }
            await window.DisposeAsync();
            window.Close();
        });
    }

    [TestMethod]
    public async Task SavedListingsLoadSearchAndShowObservationHistoryWithoutBrowser()
    {
        string path = TestDatabasePath();
        ListingStore store = new(path);
        string run = store.StartRun("https://www.avito.ru/moskva/zemelnye_uchastki", 1);
        SearchParseResult parsed = SearchParser.Parse(new(false, false, false, false, false, true,
            [new("12345678", "https://www.avito.ru/moskva/zemelnye_uchastki/item_12345678", "Участок 10 сот.", "100 000 ₽", "Москва", "Сегодня")]),
            null, null, DateTimeOffset.UtcNow);
        store.Save(run, 1, DateTimeOffset.UtcNow, parsed.Listings);
        store.UpdateRun(run, "COMPLETED", 1, 1);
        await OnStaThread(async () =>
        {
            MainWindow window = new(path);
            window.Show();
            DataGrid grid = (DataGrid)window.FindName("SavedGrid");
            Assert.AreEqual(1, grid.Items.Count);
            grid.SelectedIndex = 0;
            TextBox details = (TextBox)window.FindName("ListingDetails");
            StringAssert.Contains(details.Text, "История наблюдений");
            StringAssert.Contains(details.Text, "100 000 ₽");
            Assert.AreEqual(1, ((DataGrid)window.FindName("RunsGrid")).Items.Count);
            ((TextBox)window.FindName("DatabaseSearch")).Text = "несуществующий";
            Assert.AreEqual(0, grid.Items.Count);
            ((TextBox)window.FindName("DatabaseSearch")).Text = "12345678";
            Assert.AreEqual(1, grid.Items.Count);
            Assert.IsFalse(((Button)window.FindName("ParseButton")).IsEnabled);
            await window.DisposeAsync();
            window.Close();
        });
    }

    private static string TestDatabasePath() => Path.Combine(Path.GetTempPath(), "LandErp-SpikeTests", Guid.NewGuid().ToString("N"), "ui.sqlite");

    [TestMethod]
    public async Task WindowNotifiesCaptchaKeepsSavedDataAndContinuesOnlyOnClick()
    {
        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync(new() { Channel = "chrome", Headless = false, ChromiumSandbox = true });
        await using IBrowserContext context = await browser.NewContextAsync();
        const string content = """
            <h1>Участки</h1><div data-marker="catalog-serp" style="min-height:1000px"><div data-marker="item" data-item-id="12345678"><a data-marker="item-title" href="https://www.avito.ru/moskva/zemelnye_uchastki/item_12345678">Участок 10 сот.</a></div></div>
            """;
        await context.RouteAsync("**/*", route => route.FulfillAsync(new() { ContentType = "text/html", Body = content }));
        IPage page = await context.NewPageAsync();
        await page.GotoAsync("https://www.avito.ru/moskva/zemelnye_uchastki");
        await page.EvaluateAsync("addEventListener('scroll', () => {document.body.innerHTML = '<h1>Проверка</h1><div data-marker=\"captcha\">Подтвердите, что вы не робот</div>'}, {once:true})");
        await OnStaThread(async () =>
        {
            MainWindow window = new(TestDatabasePath(), new AvitoBrowser(page));
            window.Show();
            Button parse = (Button)window.FindName("ParseButton");
            Button resume = (Button)window.FindName("ContinueButton");
            Assert.IsTrue(parse.IsEnabled);
            parse.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntil(() => resume.IsEnabled, TimeSpan.FromSeconds(20));
            Assert.AreEqual(Visibility.Visible, ((Border)window.FindName("NoticePanel")).Visibility);
            StringAssert.Contains(((TextBlock)window.FindName("NoticeText")).Text, "Пройдите проверку");
            Assert.AreEqual(1, ((DataGrid)window.FindName("SavedGrid")).Items.Count);
            Assert.IsFalse(parse.IsEnabled);
            await page.SetContentAsync(content);
            Assert.IsTrue(resume.IsEnabled, "Manual page change must not trigger collection automatically.");
            resume.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntil(() => parse.IsEnabled, TimeSpan.FromSeconds(20));
            Assert.IsFalse(resume.IsEnabled);
            Assert.AreEqual(Visibility.Collapsed, ((Border)window.FindName("NoticePanel")).Visibility);
            Assert.AreEqual(1, ((DataGrid)window.FindName("SavedGrid")).Items.Count);
            await window.DisposeAsync();
            window.Close();
        });
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task ProvidedPagesOpenAndParseThroughSeparateButtons()
    {
        string? input = Environment.GetEnvironmentVariable("LANDERP_LIVE_TEST_URLS");
        Assert.IsFalse(string.IsNullOrWhiteSpace(input), "Explicit live URLs are required.");
        string[] urls = input!.Split('|');
        Assert.IsTrue(urls.Length is >= 1 and <= 3);
        await OnStaThread(async () =>
        {
            MainWindow window = new();
            window.Show();
            Button open = (Button)window.FindName("OpenButton");
            Button parse = (Button)window.FindName("ParseButton");
            TextBlock status = (TextBlock)window.FindName("StatusText");
            foreach (string url in urls)
            {
                ((TextBox)window.FindName("UrlInput")).Text = url;
                open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntil(() => open.IsEnabled, TimeSpan.FromSeconds(45));
                Assert.IsNull(((DataGrid)window.FindName("ResultsGrid")).ItemsSource, "Opening must not parse.");
                Console.WriteLine("OPEN_STATUS=" + status.Text);
                Assert.IsTrue(parse.IsEnabled, "Research browser did not open.");
                parse.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntil(() => parse.IsEnabled, TimeSpan.FromSeconds(20));
                Console.WriteLine("PARSE_STATUS=" + status.Text);
                Assert.IsFalse(status.Text.Contains("Ошибка браузера", StringComparison.Ordinal), "DOM read failed.");
                Assert.IsFalse(status.Text.Contains("Истекло", StringComparison.Ordinal), "DOM read timed out.");
            }
            await window.DisposeAsync();
            window.Close();
        });
    }

    private static async Task WaitUntil(Func<bool> predicate, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (!predicate())
        {
            if (DateTimeOffset.UtcNow >= deadline) Assert.Fail("UI action timeout.");
            await Task.Delay(100);
        }
    }

    private static Task OnStaThread(Func<Task> action)
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() =>
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.InvokeAsync(async () =>
            {
                try { await action(); completion.SetResult(); }
                catch (Exception error) { completion.SetException(error); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
