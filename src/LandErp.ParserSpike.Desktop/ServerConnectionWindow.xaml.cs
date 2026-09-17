using System.Windows;
namespace LandErp.ParserSpike.Desktop;
public partial class ServerConnectionWindow : Window
{
    private readonly WorkspaceController controller;
    private bool connecting;
    public ServerConnectionWindow(WorkspaceController controller)
    {
        InitializeComponent(); this.controller = controller;
        SavedText.Text = controller.SavedServerConnection() is { } saved ? "Сохранённый сервер: " + saved.Origin + ". Оставьте код пустым для повторного подключения." : "Сохранённого подключения нет.";
        Closing += (_, e) => { if (connecting) e.Cancel = true; };
    }
    private async void ConnectClick(object sender, RoutedEventArgs e)
    {
        if (connecting) return;
        connecting = true; ConnectButton.IsEnabled = CancelButton.IsEnabled = ConnectionCodeBox.IsEnabled = false; ErrorText.Text = "";
        try
        {
            string code = ConnectionCodeBox.Password.Trim();
            if (code.Length == 0) await controller.ConnectServerAsync();
            else await controller.ConnectCodeAsync(code);
            ConnectionCodeBox.Clear(); connecting = false; DialogResult = true;
        }
        catch (Exception ex) { ErrorText.Text = WorkspaceWindow.FriendlyError(ex); }
        finally { connecting = false; ConnectButton.IsEnabled = CancelButton.IsEnabled = ConnectionCodeBox.IsEnabled = true; }
    }
}
