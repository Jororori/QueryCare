using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using QueryCare.Models;
using QueryCare.ViewModels;


namespace QueryCare;

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<MainViewModel> _databases = new();
    private MainViewModel? _selected;
    private string _currentPage = "Summary";

    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(5) };
    /// <summary>Actualización automática de los datos.</summary>
    private readonly DispatcherTimer _autoRefresh = new() { Interval = TimeSpan.FromSeconds(15) };

    private static readonly Dictionary<string, (string Title, string Subtitle)> Pages = new()
    {
        ["Summary"] = ("Resumen", "Alertas prioritarias de {0}"),
        ["Queries"] = ("Consultas y procedimientos", "Análisis histórico de rendimiento de {0}"),
        ["Processes"] = ("Procesos activos", "Consultas que están ejecutándose ahora en {0}"),
        ["Indexes"] = ("Índices faltantes", "sugerencias para mejorar la eficiencia de {0}"),
        ["Blocks"] = ("Bloqueos", "Sesiones detenidas por otros procesos en {0}")
    };

    public MainWindow(IEnumerable<ConnectionSettings> connections)
    {
        InitializeComponent();
        foreach (var c in connections) _databases.Add(new MainViewModel(c));
        DatabaseList.ItemsSource = _databases;
        Select(_databases.FirstOrDefault());

        _clock.Tick += (_, _) => { foreach (var db in _databases) db.Tick(); };
        _clock.Start();
        _autoRefresh.Tick += async (_, _) => await RefreshAllAsync(silent: true);
        Loaded += async (_, _) => { await RefreshAllAsync(silent: false); _autoRefresh.Start(); };
        Closed += (_, _) => { _clock.Stop(); _autoRefresh.Stop(); };
    }

    private void Select(MainViewModel? db)
    {
        if (_selected is not null) _selected.IsSelected = false;
        _selected = db;
        if (db is not null) db.IsSelected = true;
        // Todas las secciones se enlazan a la base seleccionada.
        DataContext = db;
        ShowPage(_currentPage);
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string page } && IsInitialized) ShowPage(page);
    }

    private void ShowPage(string page)
    {
        _currentPage = page;
        if (PageSummary is null) return;
        PageSummary.Visibility = page == "Summary" ? Visibility.Visible : Visibility.Collapsed;
        PageQueries.Visibility = page == "Queries" ? Visibility.Visible : Visibility.Collapsed;
        PageProcesses.Visibility = page == "Processes" ? Visibility.Visible : Visibility.Collapsed;
        PageIndexes.Visibility = page == "Indexes" ? Visibility.Visible : Visibility.Collapsed;
        PageBlocks.Visibility = page == "Blocks" ? Visibility.Visible : Visibility.Collapsed;

        var (title, subtitle) = Pages[page];
        PageTitleText.Text = title;
        PageSubtitleText.Text = string.Format(subtitle, _selected?.DatabaseName ?? "—");
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAllAsync(silent: false);

    private async Task RefreshAllAsync(bool silent)
    {
        RefreshButton.IsEnabled = false;
        if (!silent) RefreshText.Text = "Actualizando…";
        try
        {
            // Cada base captura sus propios errores (se muestran en su franja roja y en su punto de estado).
            await Task.WhenAll(_databases.ToList().Select(db => db.RefreshAsync()));
        }
        finally
        {
            RefreshButton.IsEnabled = true;
            RefreshText.Text = "Actualizar datos";
        }
    }

    private void SelectDatabase_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MainViewModel db }) Select(db);
    }

    private async void AddDatabase_Click(object sender, RoutedEventArgs e)
    {
        var template = new ConnectionSettings { Server = _selected?.Settings.Server ?? "localhost" };
        var dialog = new ConnectionWindow(template) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null) return;

        var existing = _databases.FirstOrDefault(d => d.Settings.IsSameTarget(dialog.Result));
        if (existing is not null)
        {
            Select(existing);
            return;
        }

        var db = new MainViewModel(dialog.Result);
        _databases.Add(db);
        SaveConnections();
        Select(db);
        await db.RefreshAsync();
    }

    private void RemoveDatabase_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true; // que no seleccione la base al pulsar la X
        if (sender is not FrameworkElement { DataContext: MainViewModel db }) return;
        if (MessageBox.Show(this, $"¿Dejar de supervisar {db.DatabaseName} ({db.ServerName})?\n\nNo se modifica nada en la base de datos.",
                "Quitar base de datos", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _databases.Remove(db);
        SaveConnections();
        if (_selected == db) Select(_databases.FirstOrDefault());
        if (_databases.Count == 0) AddDatabase_Click(sender, e);
    }

    private void SaveConnections() => ConnectionStore.Save(_databases.Select(d => d.Settings));
    }
