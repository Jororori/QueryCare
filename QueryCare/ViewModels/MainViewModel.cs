using System.ComponentModel;
using System.Runtime.CompilerServices;
using QueryCare.Data;
using QueryCare.Models;

namespace QueryCare.ViewModels;

    public sealed class MainViewModel : INotifyPropertyChanged
    {
    private SqlMonitorService _service;
    private Snapshot _snapshot = new();
    private DateTime? _lastUpdate;
    private bool _isBusy;
    private string _queryFilter = "";
    private string _processFilter = "";

    private bool _isSelected;
    private string? _connectionError;

    public MainViewModel(ConnectionSettings settings)
    {
        Settings = settings;
        _service = new SqlMonitorService(settings);
        DatabaseName = settings.Database;
        ServerName = settings.Server;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ConnectionSettings Settings { get; }
    public string DatabaseName { get; private set; }
    public string ServerName { get; private set; }

    public bool IsSelected { get => _isSelected; set { _isSelected = value; OnChanged(); } }
    public bool IsBusy { get => _isBusy; private set { _isBusy = value; OnChanged(); } }
    public string ErrorText => _connectionError ?? string.Join("\n", _snapshot.Errors);
    public bool HasErrors => _connectionError is not null || _snapshot.Errors.Count > 0;

    // ---------- Estado general (para la lista de bases de datos) ----------
    /// <summary>Peor estado actual de la base: Congelado (rojo), Lento (naranja), Bien (verde) o Error.</summary>
    public string StatusLevel =>
        _connectionError is not null ? "Error"
        : _lastUpdate is null ? "Pendiente"
        : FrozenCount > 0 || BlockedCount > 0 ? "Congelado"
        : SlowCount > 0 ? "Lento"
        : "Bien";
    public string StatusText => StatusLevel switch
    {
        "Error" => "Sin conexión",
        "Pendiente" => "Conectando…",
        "Congelado" => $"{FrozenCount + BlockedCount} problema(s) grave(s)",
        "Lento" => $"{SlowCount} proceso(s) lento(s)",
        _ => "Todo en orden"
    };
    public int StatusCount => ProcessIssues + BlockedCount;

    public string UpdatedText => _lastUpdate is null
        ? "Sin datos"
        : (DateTime.Now - _lastUpdate.Value).TotalSeconds < 60
            ? $"Actualizado a las {_lastUpdate:HH:mm:ss} · automático cada 15 s"
            : $"Actualizado hace {(int)(DateTime.Now - _lastUpdate.Value).TotalMinutes} min · automático cada 15 s";

    // ---------- Procesos activos ----------
    public List<ActiveProcess> Processes => _snapshot.Processes;
    public string ProcessFilter { get => _processFilter; set { _processFilter = value; OnChanged(); OnChanged(nameof(FilteredProcesses)); } }
    public List<ActiveProcess> FilteredProcesses => Processes.Where(p => Match(_processFilter, p.SessionText, p.DisplayName, p.Login, p.Statement)).ToList();
    public int FrozenCount => Processes.Count(p => p.State == ProcessState.Congelado);
    public int SlowCount => Processes.Count(p => p.State == ProcessState.Lento);
    public int GoodCount => Processes.Count(p => p.State == ProcessState.Bien);
    public int ProcessIssues => FrozenCount + SlowCount;

    // Gráficos: cada procedimiento/consulta conocido (en ejecución o en el historial) cuenta una vez.
    // Prioridad: congelado ahora > lento ahora > lento en el historial (promedio alto) > bien.
    private List<ProcessState> _procStates = new();
    private List<ProcessState> _adHocStates = new();
    public int ProcGood => _procStates.Count(s => s == ProcessState.Bien);
    public int ProcSlow => _procStates.Count(s => s == ProcessState.Lento);
    public int ProcFrozen => _procStates.Count(s => s == ProcessState.Congelado);
    public int ProcTotal => _procStates.Count;
    public int AdHocGood => _adHocStates.Count(s => s == ProcessState.Bien);
    public int AdHocSlow => _adHocStates.Count(s => s == ProcessState.Lento);
    public int AdHocFrozen => _adHocStates.Count(s => s == ProcessState.Congelado);
    public int AdHocTotal => _adHocStates.Count;

    public string ProcessesSubtitle => $"{FrozenCount} detenido{(FrozenCount == 1 ? "" : "s")} · {Processes.Count - FrozenCount} en ejecución";

    // ---------- Consultas y procedimientos ----------
    public List<QueryStat> QueryStats => _snapshot.QueryStats;
    public string QueryFilter { get => _queryFilter; set { _queryFilter = value; OnChanged(); OnChanged(nameof(FilteredQueryStats)); OnChanged(nameof(QueryFooter)); } }
    public List<QueryStat> FilteredQueryStats => QueryStats.Where(q => Match(_queryFilter, q.Name, q.Text, q.TypeLabel)).ToList();
    public int AdHocAnalyzed => _snapshot.AdHocCount;
    public int ProceduresAnalyzed => _snapshot.ProcedureCount;
    public int HighTimeCount => QueryStats.Count(q => q.AvgMs >= Thresholds.HighAvgMs);
    public string QueryFooter => $"Mostrando {FilteredQueryStats.Count} resultados de {DatabaseName}";

    // ---------- Bloqueos ----------
    public List<ActiveProcess> BlockedProcesses => Processes.Where(p => p.IsBlocked).OrderByDescending(p => p.WaitTimeMs).ToList();
    public int BlockedCount => BlockedProcesses.Count;
    public string BlockedSubtitle => BlockedCount == 0 ? "Sin esperas" : $"Espera máxima de {Fmt.Long(BlockedProcesses.Max(p => p.WaitTimeMs))}";
    public string BlockedBadge => $"{BlockedCount} bloqueo{(BlockedCount == 1 ? "" : "s")} activo{(BlockedCount == 1 ? "" : "s")}";
    public List<BlockingChain> Chains { get; private set; } = new();

    // ---------- Índices ----------
    public List<MissingIndex> MissingIndexes => _snapshot.MissingIndexes;
    public int MissingIndexCount => MissingIndexes.Count;
    public string IndexesSubtitle => MissingIndexCount == 0 ? "Sin sugerencias" : $"Impacto estimado hasta {MissingIndexes.Max(i => i.Impact):0}%";
    public string IndexesBanner => $"{MissingIndexCount} Sugerencia{(MissingIndexCount == 1 ? "" : "es")} de mejora detectada{(MissingIndexCount == 1 ? "" : "s")}";

    // ---------- Alertas ----------
    public List<AlertItem> Alerts { get; private set; } = new();
    public int AlertCount => Alerts.Count;
    public string AlertsSubtitle
    {
        get
        {
            int c = Alerts.Count(a => a.Severity == "CRÍTICA"), h = Alerts.Count(a => a.Severity == "ALTA"), m = Alerts.Count(a => a.Severity == "MEDIA");
            var parts = new List<string> { $"{c} crítica{(c == 1 ? "" : "s")}", $"{h} alta{(h == 1 ? "" : "s")}" };
            if (m > 0) parts.Add($"{m} media{(m == 1 ? "" : "s")}");
            return string.Join(" · ", parts);
        }
    }
    public bool HasNoAlerts => _lastUpdate is not null && Alerts.Count == 0;

    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            try
            {
                _snapshot = await _service.GetSnapshotAsync();
                _connectionError = _snapshot.Errors.Count == 3 ? "No se pudo leer ninguna sección de esta base de datos." : null;
            }
            catch (Exception ex)
            {
                _snapshot = new Snapshot();
                _connectionError = $"No se pudo conectar a {ServerName} / {DatabaseName}: {ex.Message}";
            }
            _lastUpdate = DateTime.Now;
            Chains = BuildChains(Processes);
            BuildChartStates();
            Alerts = BuildAlerts();
            OnChanged(string.Empty); // refresca todas las propiedades
        }
        finally { IsBusy = false; }
    }

    public void Tick() => OnChanged(nameof(UpdatedText));

    private void BuildChartStates()
    {
        static ProcessState Worst(ProcessState a, ProcessState b) => a > b ? a : b;
        static ProcessState Historic(QueryStat q) => q.AvgMs >= Thresholds.HighAvgMs ? ProcessState.Lento : ProcessState.Bien;

        var procs = new Dictionary<string, ProcessState>(StringComparer.OrdinalIgnoreCase);
        foreach (var q in QueryStats.Where(q => q.IsProcedure))
            procs[q.Name] = procs.TryGetValue(q.Name, out var s) ? Worst(s, Historic(q)) : Historic(q);
        foreach (var p in Processes.Where(p => p.IsProcedure))
            procs[p.ObjectName!] = procs.TryGetValue(p.ObjectName!, out var s) ? Worst(s, p.State) : p.State;
        _procStates = procs.Values.ToList();

        // Las consultas ad hoc en ejecución no se pueden emparejar con el historial de forma fiable: se suman ambas.
        _adHocStates = QueryStats.Where(q => !q.IsProcedure).Select(Historic)
            .Concat(Processes.Where(p => !p.IsProcedure).Select(p => p.State))
            .ToList();
    }

    private static List<BlockingChain> BuildChains(List<ActiveProcess> processes)
    {
        var bySession = processes.GroupBy(p => p.SessionId).ToDictionary(g => g.Key, g => g.First());
        var blocked = processes.Where(p => p.IsBlocked).ToList();
        // Cabezas: sesiones que bloquean a otras y no están bloqueadas (pueden estar inactivas con transacción abierta).
        var heads = blocked.Select(p => p.BlockingSessionId).Distinct()
            .Where(id => !bySession.TryGetValue(id, out var bp) || !bp.IsBlocked).ToList();

        var chains = new List<BlockingChain>();
        foreach (var head in heads)
        {
            var ordered = new List<ActiveProcess>();
            var visited = new HashSet<int> { head };
            var queue = new Queue<int>(new[] { head });
            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                foreach (var child in blocked.Where(b => b.BlockingSessionId == id && visited.Add(b.SessionId)))
                {
                    ordered.Add(child);
                    queue.Enqueue(child.SessionId);
                }
            }
            var nodes = new List<ChainNode> { new() { Title = $"Sesión {head}", Subtitle = "Bloqueador principal", IsHead = true } };
            nodes.AddRange(ordered.Select(o => new ChainNode { Title = $"Sesión {o.SessionId}", Subtitle = $"Bloqueada · {o.WaitText}", ShowArrow = true }));
            chains.Add(new BlockingChain { Nodes = nodes });
        }
        return chains;
    }

    private List<AlertItem> BuildAlerts()
    {
        var alerts = new List<AlertItem>();

        foreach (var p in Processes.Where(p => p.State == ProcessState.Congelado))
        {
            var what = p.IsProcedure ? $"El procedimiento {p.ObjectName}" : "Una consulta";
            var on = string.IsNullOrEmpty(p.BlockedObject) ? "" : $" sobre {p.BlockedObject}";
            alerts.Add(new AlertItem
            {
                Rank = 0, Severity = "CRÍTICA", Glyph = "",
                Title = $"{what} no responde",
                Description = $"La sesión {p.SessionId} lleva {Fmt.Long(p.ElapsedMs)} sin avanzar. Está esperando {Fmt.WaitDescription(p.WaitType)}{on}.",
                Tags = new() { $"Sesión {p.SessionId}", p.WaitTypeText, p.Login }
            });
        }

        foreach (var chain in Chains)
        {
            var headId = int.Parse(chain.Nodes[0].Title.Split(' ')[1]);
            var victims = Processes.Where(p => p.IsBlocked && chain.Nodes.Skip(1).Any(n => n.Title == $"Sesión {p.SessionId}")).ToList();
            var obj = victims.Select(v => v.BlockedObject).FirstOrDefault(o => !string.IsNullOrEmpty(o)) ?? "la base de datos";
            var path = string.Join(", que a su vez bloquea a la ", victims.Select(v => v.SessionId));
            alerts.Add(new AlertItem
            {
                Rank = 1, Severity = "ALTA", Glyph = "",
                Title = $"Cadena de bloqueo activa en {obj}",
                Description = $"La sesión {headId} bloquea a la {path}. {victims.Count} proceso{(victims.Count == 1 ? "" : "s")} no pueden continuar.",
                Tags = new() { $"{victims.Count} bloqueado{(victims.Count == 1 ? "" : "s")}", obj, $"Espera máxima {Fmt.Duration(victims.Max(v => v.WaitTimeMs))}" }
            });
        }

        foreach (var p in Processes.Where(p => p.State == ProcessState.Lento))
        {
            alerts.Add(new AlertItem
            {
                Rank = 1, Severity = "ALTA", Glyph = "",
                Title = $"{(p.IsProcedure ? p.ObjectName : "Una consulta")} está tardando demasiado",
                Description = $"La sesión {p.SessionId} lleva {Fmt.Long(p.ElapsedMs)} en ejecución. Espera actual: {Fmt.WaitDescription(p.WaitType)}.",
                Tags = new() { $"Sesión {p.SessionId}", p.WaitTypeText, p.Login }
            });
        }

        foreach (var i in MissingIndexes.Where(i => i.Impact >= 90).Take(3))
        {
            alerts.Add(new AlertItem
            {
                Rank = 2, Severity = "MEDIA", Glyph = "",
                Title = $"Falta un índice en {i.Table}",
                Description = $"SQL Server estima una mejora del {i.ImpactText} en las consultas que filtran por {i.KeyColumns}.",
                Tags = new() { i.Table, $"{i.PotentialUses:N0} usos potenciales" }
            });
        }

        return alerts.OrderBy(a => a.Rank).ToList();
    }

    private static bool Match(string filter, params string?[] fields) =>
        string.IsNullOrWhiteSpace(filter) || fields.Any(f => f?.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase) == true);

    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

