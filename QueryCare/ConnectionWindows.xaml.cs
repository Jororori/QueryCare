using System.Windows;
using QueryCare.Data;
using QueryCare.Models;

namespace QueryCare;

public partial class ConnectionWindow : Window
{
    public ConnectionSettings? Result { get; private set; }
    public string ServerName { get; private set; } = "";

    public ConnectionWindow(ConnectionSettings settings)
    {
        InitializeComponent();
        ServerBox.Text = settings.Server;
        DatabaseBox.Text = settings.Database;
        UserBox.Text = settings.User;
        TrustCert.IsChecked = settings.TrustServerCertificate;
        (settings.UseWindowsAuth ? WindowsAuth : SqlAuth).IsChecked = true;
        Loaded += (_, _) => (string.IsNullOrEmpty(DatabaseBox.Text) ? DatabaseBox : ServerBox).Focus();
    }

    private void Auth_Changed(object sender, RoutedEventArgs e)
    {
        if (SqlCredentials is not null)
            SqlCredentials.Visibility = SqlAuth.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        var settings = new ConnectionSettings
        {
            Server = ServerBox.Text.Trim(),
            Database = DatabaseBox.Text.Trim(),
            UseWindowsAuth = WindowsAuth.IsChecked == true,
            User = UserBox.Text.Trim(),
            Password = PasswordBox.Password,
            TrustServerCertificate = TrustCert.IsChecked == true
        };
        if (settings.Server.Length == 0 || settings.Database.Length == 0)
        {
            ShowError("Indica el servidor y la base de datos.");
            return;
        }

        ConnectButton.IsEnabled = false;
        ConnectButton.Content = "Conectando…";
        ErrorPanel.Visibility = Visibility.Collapsed;
        try
        {
            var (server, _, canViewState) = await new SqlMonitorService(settings).TestAsync();
            if (!canViewState &&
                MessageBox.Show(this,
                    "El usuario no tiene el permiso VIEW SERVER STATE, por lo que no se podrán leer procesos, consultas ni índices.\n\n¿Continuar de todas formas?",
                    "Permisos insuficientes", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            Result = settings;
            ServerName = server;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            ConnectButton.IsEnabled = true;
            ConnectButton.Content = "Conectar";
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
    }
}
