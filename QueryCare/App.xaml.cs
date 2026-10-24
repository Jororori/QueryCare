using System.Configuration;
using System.Data;
using System.Windows;
using QueryCare.Models;

namespace QueryCare;

    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var connections = ConnectionStore.Load();
        if (connections.Count == 0)
        {
            // Primera vez: se pide al menos una base de datos.
            var dialog = new ConnectionWindow(new ConnectionSettings());
            if (dialog.ShowDialog() != true || dialog.Result is null)
            {
                Shutdown();
                return;
            }
            connections.Add(dialog.Result);
            ConnectionStore.Save(connections);
        }

        var main = new MainWindow(connections);
        MainWindow = main;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        main.Show();
    }
}


