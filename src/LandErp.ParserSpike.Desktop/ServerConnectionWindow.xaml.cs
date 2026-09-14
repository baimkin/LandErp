using System.Windows;
using LandErp.ParserSpike.ServerIntegration;

namespace LandErp.ParserSpike.Desktop;

public partial class ServerConnectionWindow : Window
{
    private readonly WorkspaceController controller;
    private bool connecting;
    public ServerConnectionWindow(WorkspaceController controller)
    {
        InitializeComponent();
        this.controller = controller;
        ServerConnection? saved = controller.SavedServerConnection();
        if (saved != null)
        { AddressBox.Text = saved.Origin.AbsoluteUri; AgentBox.Text = saved.AgentId.ToString(); KeyBox.Password = saved.Token; }
        Closing += (_, args) => { if (connecting) args.Cancel = true; };
    }
    private async void ConnectClick(object sender, RoutedEventArgs e)
    {
        if (connecting) return;
        ErrorText.Text = "";
        if (!Uri.TryCreate(AddressBox.Text.Trim(), UriKind.Absolute, out Uri? origin)
            || origin.Scheme != Uri.UriSchemeHttps || origin.UserInfo.Length != 0
            || origin.AbsolutePath != "/" || origin.Query.Length != 0 || origin.Fragment.Length != 0)
        { ErrorText.Text = "Укажите HTTPS адрес сервера, например https://localhost:7240/."; return; }
        if (!Guid.TryParse(AgentBox.Text.Trim(), out Guid id) || id == Guid.Empty)
        { ErrorText.Text = "Скопируйте ID сборщика из ERP."; return; }
        string key = KeyBox.Password.Trim();
        if (key.Length != 64 || !key.All(char.IsAsciiHexDigit))
        { ErrorText.Text = "Скопируйте полный ключ доступа, выданный ERP при создании или перевыпуске."; return; }
        connecting = true; ConnectButton.IsEnabled = false; CancelButton.IsEnabled = false;
        AddressBox.IsEnabled = false; AgentBox.IsEnabled = false; KeyBox.IsEnabled = false;
        ConnectButton.Content = "Подключаемся…";
        try
        {
            await controller.ConnectServerAsync(new(origin, id, key));
            connecting = false; DialogResult = true;
        }
        catch (ServerDeliveryException exception)
        {
            ErrorText.Text = exception.Code switch
            {
                "AGENT_UNAUTHORIZED" => "Сервер не принял ID или ключ доступа. Проверьте их; отозванный ключ нужно перевыпустить в ERP.",
                "RETRY_LATER" => "Сервер просит подождать. Повторите подключение немного позже.",
                _ => "Не удалось подключиться. Проверьте адрес, запуск сервера и доверие HTTPS сертификату на этом компьютере."
            };
        }
        catch (InvalidOperationException)
        { ErrorText.Text = controller.Runner.IsRunning ? "Сначала остановите или завершите текущий сбор." : "Не удалось сменить подключение. Завершите доставку прежнего задания перед сменой сервера или ID сборщика."; }
        catch (Exception)
        { ErrorText.Text = "Не удалось сохранить подключение. Проверьте доступ к локальной папке данных и повторите."; }
        finally
        {
            connecting = false; ConnectButton.IsEnabled = true; CancelButton.IsEnabled = true;
            AddressBox.IsEnabled = true; AgentBox.IsEnabled = true; KeyBox.IsEnabled = true;
            ConnectButton.Content = "Подключиться";
        }
    }
}
