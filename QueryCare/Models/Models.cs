using System.Text.RegularExpressions;

namespace QueryCare.Models;

    public enum ProcessState { Bien, Lento, Congelado }

    public static class Thresholds
    {
        public const long BlockedFrozenMs = 60_000;
        public const long WaitFrozenMs = 300_000;
        public const long SlowMs = 30_000;
        public const double HighAvgMs = 10_000;
    }

    public static class Fmt
    {
        public static string Duration(double ms)
        {
            var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
            if (t.TotalHours >= 1) return $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00} h";
            if (t.TotalMinutes >= 1) return $"{(int)t.TotalMinutes:00}:{t.Seconds:00} min";
            if (t.TotalSeconds >= 1) return $"{t.TotalSeconds:0.0} s";
            return $"{ms:0} ms";
        }

        public static string Long(double ms)
        {
            var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
            if (t.TotalHours >= 1) return $"{(int)t.TotalHours} h {t.Minutes} min";
            if (t.TotalMinutes >= 1) return $"{(int)t.TotalMinutes} min {t.Seconds} s";
            return $"{t.Seconds} s";
        }

        public static string OneLine(string? sql, int max)
        {
            if (string.IsNullOrWhiteSpace(sql)) return "";
            var s = Regex.Replace(sql, @"\s+", " ").Trim();
            return s.Length > max ? s[..max] + "…" : s;
        }

        public static string WaitDescription(string? wait) => wait switch
        {
            null or "" => "un recurso",
            "LCK_M_X" or "LCK_M_IX" => "un bloqueo exclusivo",
            "LCK_M_S" or "LCK_M_IS" => "un bloqueo compartido",
            "LCK_M_U" or "LCK_M_IU" => "un bloqueo de actualización",
            "LCK_M_SCH_S" or "LCK_M_SCH_M" => "un bloqueo de esquema",
            _ when wait.StartsWith("PAGEIOLATCH") => "lecturas de disco",
            _ when wait.StartsWith("LCK_") => "un bloqueo",
            "CXPACKET" or "CXCONSUMER" => "hilos en paralelo",
            "ASYNC_NETWORK_IO" => "que la aplicación lea los resultados",
            "WRITELOG" => "la escritura del log de transacciones",
            "RESOURCE_SEMAPHORE" => "memoria disponible",
            "SOS_SCHEDULER_YIELD" => "tiempo de CPU",
            _ => wait
        };
    }


    public sealed class ActiveProcess
    {
        public int SessionId { get; init; }
        public int BlockingSessionId { get; init; }
        public string Status { get; init; } = "";
        public string? WaitType { get; init; }
        public long WaitTimeMs { get; init; }
        public long ElapsedMs { get; init; }
        public long CpuMs { get; init; }
        public long LogicalReads { get; init; }
        public string Login { get; init; } = "";
        public string Program { get; init; } = "";
        public string Host { get; init; } = "";
        public string? ObjectName { get; init; }
        public string? Statement { get; init; }
        public string? BlockedObject { get; init; }
        public string? BlockerLogin { get; init; }

        public bool IsProcedure => !string.IsNullOrEmpty(ObjectName);
        public bool IsBlocked => BlockingSessionId > 0;

        public ProcessState State =>
            (IsBlocked && WaitTimeMs >= Thresholds.BlockedFrozenMs) || WaitTimeMs >= Thresholds.WaitFrozenMs
                ? ProcessState.Congelado
                : ElapsedMs >= Thresholds.SlowMs ? ProcessState.Lento : ProcessState.Bien;

        public string Level => State.ToString();
        public string StateLabel => State switch
        {
            ProcessState.Congelado => "Sin respuesta",
            ProcessState.Lento => "Cargando demasiado",
            _ => "Ejecutando"
        };

        public string SessionText => $"#{SessionId}";
        public string DisplayName => IsProcedure ? ObjectName! : Fmt.OneLine(Statement, 60);
        public string Snippet => Fmt.OneLine(Statement, 120);
        public string ElapsedText => Fmt.Duration(ElapsedMs);
        public string WaitText => Fmt.Duration(WaitTimeMs);
        public string WaitTypeText => string.IsNullOrEmpty(WaitType) ? "—" : WaitType;
        public string BlockingText => $"#{BlockingSessionId}";
        public string BlockedObjectText => string.IsNullOrEmpty(BlockedObject) ? "—" : BlockedObject;
        public string BlockerLoginText => string.IsNullOrEmpty(BlockerLogin) ? "—" : BlockerLogin;
        public string Details => $"Estado SQL: {Status} · CPU: {Fmt.Duration(CpuMs)} · Lecturas: {LogicalReads:N0} · Host: {Host} · Programa: {Program}";
    }

    public sealed class QueryStat
    {
        public bool IsProcedure { get; init; }
        public string Name { get; init; } = "";
        public string Text { get; init; } = "";
        public long Executions { get; init; }
        public double AvgMs { get; init; }
        public double MaxMs { get; init; }
        public double CpuTotalMs { get; init; }
        public long LogicalReads { get; init; }

        public string TypeLabel => IsProcedure ? "Procedimiento" : "Consulta";
        public string AvgText => Fmt.Duration(AvgMs);
        public string MaxText => Fmt.Duration(MaxMs);
        public string Snippet => Fmt.OneLine(Text, 120);
        public string Level => AvgMs >= 300_000 ? "Crítico" : AvgMs >= 60_000 ? "Alto" : AvgMs >= Thresholds.HighAvgMs ? "Medio" : "Bajo";
        public string Details => $"Tiempo máximo: {MaxText} · CPU total: {Fmt.Duration(CpuTotalMs)}";
    }

    public sealed class MissingIndex
    {
        public string Table { get; init; } = "";
        public string? EqualityColumns { get; init; }
        public string? InequalityColumns { get; init; }
        public string? IncludedColumns { get; init; }
        public long Seeks { get; init; }
        public long Scans { get; init; }
        public double AvgCost { get; init; }
        public double Impact { get; init; }

        public long PotentialUses => Seeks + Scans;
        public string KeyColumns => Clean(string.Join(", ", new[] { EqualityColumns, InequalityColumns }.Where(s => !string.IsNullOrWhiteSpace(s))));
        public string IncludedText => string.IsNullOrWhiteSpace(IncludedColumns) ? "—" : Clean(IncludedColumns);
        public string ImpactText => $"{Impact:0.0}%";
        public string Level => Impact >= 80 ? "Alto" : Impact >= 50 ? "Medio" : "Bajo";
        public string Tooltip => $"Igualdad: {Clean(EqualityColumns) ?? "—"}\nRango: {Clean(InequalityColumns) ?? "—"}\nIncluidas: {IncludedText}\nCosto promedio: {AvgCost:N2}";

        private static string Clean(string? s) => s?.Replace("[", "").Replace("]", "") ?? "";
    }

    public sealed class AlertItem
    {
        public int Rank { get; init; }
        public string Severity { get; init; } = "";
        public string Glyph { get; init; } = "";
        public string Title { get; init; } = "";
        public string Description { get; init; } = "";
        public List<string> Tags { get; init; } = new();
    }

    public sealed class ChainNode
    {
        public string Title { get; init; } = "";
        public string Subtitle { get; init; } = "";
        public bool IsHead { get; init; }
        public bool ShowArrow { get; init; }
    }

    public sealed class BlockingChain
    {
        public List<ChainNode> Nodes { get; init; } = new();
    }

   
    public sealed class Snapshot
    {
        public List<ActiveProcess> Processes { get; set; } = new();
        public List<QueryStat> QueryStats { get; set; } = new();
        public List<MissingIndex> MissingIndexes { get; set; } = new();
        public int AdHocCount { get; set; }
        public int ProcedureCount { get; set; }
        public List<string> Errors { get; } = new();
    }






















