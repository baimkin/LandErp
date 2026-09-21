using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LandErp.ParserSpike.Contracts;
using LandErp.ParserSpike.Desktop;
using LandErp.ParserSpike.LocalCollection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.ParserSpike.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ListingDetailsUiTests
{
    private sealed class NoSessions : ISourceSessions
    {
        public Task<ISourcePage> CreatePageAsync(SourceSite source, CollectionSettings settings, CancellationToken cancellationToken)
            => throw new InvalidOperationException("NO_BROWSER_EXPECTED");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [TestMethod]
    public async Task PhotoGalleryUsesUniformPreviewNavigationAndThumbnails()
    {
        await OnSta(() =>
        {
            ListingDetailsControl control = new();
            Image preview = (Image)control.FindName("PhotoPreview");
            Button previous = (Button)control.FindName("PreviousPhotoButton");
            Button next = (Button)control.FindName("NextPhotoButton");
            ListBox thumbnails = (ListBox)control.FindName("PhotoThumbnails");
            TextBlock position = (TextBlock)control.FindName("PhotoPositionText");
            Assert.AreEqual(System.Windows.Media.Stretch.Uniform, preview.Stretch);
            Assert.IsNotNull(previous);
            Assert.IsNotNull(next);
            Assert.IsNotNull(thumbnails);
            Assert.IsNotNull(position);
            Assert.IsNotNull(control.FindName("PhotoEmptyPanel"));
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task DetailsModePersistsAndOneSeparateWindowFollowsTableSelection()
    {
        string root = Path.Combine(Path.GetTempPath(), "LandErp-DetailsUi", Guid.NewGuid().ToString("N"));
        string database = Path.Combine(root, "data.sqlite");
        WorkspaceController controller = new(database, root, new NoSessions());
        SearchLink link = controller.Store.SaveLink("Cian", "https://www.cian.ru/cat.php?deal_type=sale&offer_type=suburban&object_type%5B0%5D=3");
        string batch = controller.Store.StartBatch(new(), force: true, onlyLinkId: link.Id);
        CollectionJob job = controller.Store.Claim(batch, SourceSite.Cian, "fixture")!;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ListingObservation[] items =
        [
            Item("334043735", now, "Первый"),
            Item("332776719", now.AddSeconds(1), "Второй")
        ];
        controller.Store.SavePage(job, 1, link.Url, items, true, new(NextKind.End), "fixture");
        controller.Store.SetState(job, JobState.Completed, "fixture");
        controller.SetListingDetailsMode(ListingDetailsDisplayMode.SeparateWindow);
        controller.SetListingDetailsWidth(520);

        await OnSta(async () =>
        {
            WorkspaceWindow window = new(controller); window.Show();
            try
            {
                TabControl tabs = (TabControl)window.FindName("Pages");
                DataGrid grid = (DataGrid)window.FindName("ListingsGrid");
                await Until(() => grid.Items.Count == 2);
                tabs.SelectedIndex = 2;
                grid.SelectedIndex = 0;
                await Until(() => window.OwnedWindows.OfType<ListingDetailsWindow>().Count() == 1);
                ListingDetailsWindow firstWindow = window.OwnedWindows.OfType<ListingDetailsWindow>().Single();
                string firstId = ((ListingRow)grid.SelectedItem).ExternalId;
                Assert.AreEqual(firstId, firstWindow.CurrentExternalId);

                grid.SelectedIndex = 1;
                await Until(() => firstWindow.CurrentExternalId == ((ListingRow)grid.SelectedItem).ExternalId);
                Assert.AreSame(firstWindow, window.OwnedWindows.OfType<ListingDetailsWindow>().Single());

                firstWindow.Close();
                await Until(() => window.OwnedWindows.OfType<ListingDetailsWindow>().Count() == 0);
                grid.SelectedIndex = 0;
                await Until(() => window.OwnedWindows.OfType<ListingDetailsWindow>().Count() == 1);
                ListingDetailsWindow reopened = window.OwnedWindows.OfType<ListingDetailsWindow>().Single();
                Assert.AreNotSame(firstWindow, reopened);
                Assert.AreEqual(((ListingRow)grid.SelectedItem).ExternalId, reopened.CurrentExternalId);
            }
            finally { window.Close(); await Until(() => !window.IsVisible); }
        });

        await using WorkspaceController reopenedController = new(database, root, new NoSessions());
        Assert.AreEqual(ListingDetailsDisplayMode.SeparateWindow, reopenedController.ListingDetailsMode);
        Assert.AreEqual(520d, reopenedController.ListingDetailsWidth, 0.1);
    }

    private static ListingObservation Item(string id, DateTimeOffset time, string title) => new()
    {
        Source = SourceSite.Cian,
        ExternalId = id,
        Url = $"https://www.cian.ru/sale/suburban/{id}/",
        ObservedAtUtc = time,
        Title = TextValue.Read(title),
        Price = NumberValue.Read("1 000 000 ₽"),
        AreaSquareMeters = new(Presence.Present, "10 сот.", 1000),
        DeclaredLandTypes = [LandType.Izhs],
        InferredLandTypes = [LandType.Izhs]
    };

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
