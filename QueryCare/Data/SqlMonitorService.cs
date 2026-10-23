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
    private Task<List<ActiveProcess>> GetProcessesAsync(CancellationToken ct)
    {
        const string sql = """
            SELECT r.session_id, r.blocking_session_id, r.status, r.wait_type, r.wait_time,
                   r.total_elapsed_time, r.cpu_time, r.logical_reads,
                   s.login_name, s.program_name, s.host_name,
                   OBJECT_SCHEMA_NAME(t.objectid, t.dbid) + '.' + OBJECT_NAME(t.objectid, t.dbid) AS object_name,
                   SUBSTRING(t.text, (r.statement_start_offset / 2) + 1,
                       ((CASE r.statement_end_offset WHEN -1 THEN DATALENGTH(t.text) ELSE r.statement_end_offset END
                         - r.statement_start_offset) / 2) + 1) AS statement_text,
                   lk.blocked_object,
                   bs.login_name AS blocker_login
            FROM sys.dm_exec_requests r
            JOIN sys.dm_exec_sessions s ON s.session_id = r.session_id
            OUTER APPLY sys.dm_exec_sql_text(r.sql_handle) t
            OUTER APPLY (
                SELECT TOP (1) COALESCE(
                    OBJECT_SCHEMA_NAME(p.object_id) + '.' + OBJECT_NAME(p.object_id),
                    CASE WHEN l.resource_type = 'OBJECT'
                         THEN OBJECT_SCHEMA_NAME(l.resource_associated_entity_id) + '.' + OBJECT_NAME(l.resource_associated_entity_id) END
                ) AS blocked_object
                FROM sys.dm_tran_locks l
                LEFT JOIN sys.partitions p ON p.hobt_id = l.resource_associated_entity_id
                WHERE l.request_session_id = r.session_id AND l.request_status = 'WAIT'
            ) lk
            LEFT JOIN sys.dm_exec_sessions bs ON bs.session_id = r.blocking_session_id
            WHERE r.session_id <> @@SPID
              AND r.database_id = DB_ID()
              AND s.is_user_process = 1
            ORDER BY r.total_elapsed_time DESC;
            """;
        return QueryAsync(sql, r => new ActiveProcess
        {
            SessionId = I(r, 0),
            BlockingSessionId = I(r, 1),
            Status = S(r, 2),
            WaitType = N(r, 3),
            WaitTimeMs = L(r, 4),
            ElapsedMs = L(r, 5),
            CpuMs = L(r, 6),
            LogicalReads = L(r, 7),
            Login = S(r, 8),
            Program = S(r, 9),
            Host = S(r, 10),
            ObjectName = N(r, 11),
            Statement = N(r, 12),
            BlockedObject = N(r, 13),
            BlockerLogin = N(r, 14)
        }, ct);
    }

}

