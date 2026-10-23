using Microsoft.Data.SqlClient;
using QueryCare.Models;

namespace QueryCare.Data;

public sealed class SqlMonitorService
{
    private const string Preamble = "/* QueryCare */ SET NOCOUNT ON; SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED; SET LOCK_TIMEOUT 5000;\n";

    private readonly string _connectionString;

    public SqlMonitorService(ConnectionSettings settings) => _connectionString = settings.BuildConnectionString();

    public async Task<(string Server, string Database, bool CanViewState)> TestAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT @@SERVERNAME, DB_NAME(),
                   CAST(ISNULL(HAS_PERMS_BY_NAME(NULL, NULL, 'VIEW SERVER STATE'), 0) AS int);
            """;
        var rows = await QueryAsync(sql, r => (S(r, 0), S(r, 1), I(r, 2) == 1), ct);
        return rows[0];
    }

    public async Task<Snapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        var snap = new Snapshot();
        await Run(snap, "procesos activos", async () => snap.Processes = await GetProcessesAsync(ct));
        await Run(snap, "consultas y procedimientos", async () =>
        {
            var procs = await GetProcedureStatsAsync(ct);
            var adhoc = await GetAdHocStatsAsync(ct);
            snap.QueryStats = procs.Concat(adhoc).OrderByDescending(q => q.AvgMs).ToList();
            (snap.ProcedureCount, snap.AdHocCount) = await GetCountsAsync(ct);
        });
        await Run(snap, "Ã­ndices faltantes", async () => snap.MissingIndexes = await GetMissingIndexesAsync(ct));
        return snap;
    }

    private static async Task Run(Snapshot snap, string area, Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { snap.Errors.Add($"No se pudo leer {area}: {ex.Message}"); }
    }
}

