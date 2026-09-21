using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace LandErp.ParserSpike.Desktop;

public partial class PhotoViewerWindow : Window
{
    private readonly string[] urls;
    private bool changingSelection;
    public int SelectedIndex { get; private set; }

    public PhotoViewerWindow(string[] photoUrls, int selectedIndex)
    {
        if (photoUrls.Length == 0) throw new ArgumentException("PHOTO_URLS_REQUIRED", nameof(photoUrls));
        InitializeComponent();
        urls = photoUrls.ToArray();
        ViewerThumbnails.ItemsSource = urls.Select((url, index) => new PhotoThumbnailItem(index, CreateImage(url, 220))).ToArray();
        ShowPhoto(Math.Clamp(selectedIndex, 0, urls.Length - 1));
    }

    private static BitmapImage CreateImage(string url, int decodeWidth = 0)
    {
        BitmapImage image = new();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnDemand;
        image.UriSource = new Uri(url, UriKind.Absolute);
        if (decodeWidth > 0) image.DecodePixelWidth = decodeWidth;
        image.EndInit();
        return image;
    }

    private void ShowPhoto(int index)
    {
        SelectedIndex = Math.Clamp(index, 0, urls.Length - 1);
        ViewerImage.Source = CreateImage(urls[SelectedIndex]);
        ViewerCounter.Text = $"Фото {SelectedIndex + 1} из {urls.Length}";
        ViewerPreviousButton.IsEnabled = SelectedIndex > 0;
        ViewerNextButton.IsEnabled = SelectedIndex < urls.Length - 1;
        changingSelection = true;
        ViewerThumbnails.SelectedIndex = SelectedIndex;
        if (ViewerThumbnails.SelectedItem is not null) ViewerThumbnails.ScrollIntoView(ViewerThumbnails.SelectedItem);
        changingSelection = false;
    }

    private void ViewerPreviousClick(object sender, RoutedEventArgs e) => ShowPhoto(SelectedIndex - 1);
    private void ViewerNextClick(object sender, RoutedEventArgs e) => ShowPhoto(SelectedIndex + 1);

    private void ViewerThumbnailSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!changingSelection && ViewerThumbnails.SelectedItem is PhotoThumbnailItem item) ShowPhoto(item.Index);
    }

    private void ViewerKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Left) { ShowPhoto(SelectedIndex - 1); e.Handled = true; }
        else if (e.Key == Key.Right) { ShowPhoto(SelectedIndex + 1); e.Handled = true; }
        else if (e.Key == Key.Escape) { Close(); e.Handled = true; }
    }
}
