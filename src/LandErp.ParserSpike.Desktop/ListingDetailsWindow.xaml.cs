using System.Windows;
using LandErp.ParserSpike.LocalCollection;

namespace LandErp.ParserSpike.Desktop;

public partial class ListingDetailsWindow : Window
{
    public string? CurrentExternalId => DetailsView.CurrentExternalId;
    public event EventHandler<ListingOpenEventArgs>? OpenListingRequested;
    public ListingDetailsWindow()
    {
        InitializeComponent();
        DetailsView.OpenListingRequested += (_, args) => OpenListingRequested?.Invoke(this, args);
    }
    public void ShowListing(ListingRow row, HistoryRow[] history, string context)
    {
        DetailsView.ShowListing(row, history, context);
        Title = $"Карточка объявления · {row.Source} · {row.ExternalId}";
    }
    public void Clear() => DetailsView.Clear();
}
