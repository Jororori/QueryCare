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
    private Task<List<QueryStat>> GetProcedureStatsAsync(CancellationToken ct)
    {
        // Tiempos de las DMVs en microsegundos -> se convierten a ms.
        const string sql = """
            SELECT TOP (100)
                   OBJECT_SCHEMA_NAME(ps.object_id, ps.database_id) + '.' + OBJECT_NAME(ps.object_id, ps.database_id) AS name,
                   ps.execution_count,
                   ps.total_elapsed_time / 1000.0 / ps.execution_count AS avg_ms,
                   ps.max_elapsed_time / 1000.0 AS max_ms,
                   ps.total_worker_time / 1000.0 AS cpu_ms,
                   ps.total_logical_reads
            FROM sys.dm_exec_procedure_stats ps
            WHERE ps.database_id = DB_ID() AND ps.execution_count > 0
            ORDER BY ps.total_elapsed_time / ps.execution_count DESC;
            """;
        return QueryAsync(sql, r => new QueryStat
        {
            IsProcedure = true,
            Name = S(r, 0),
            Text = "EXEC " + S(r, 0),
            Executions = L(r, 1),
            AvgMs = D(r, 2),
            MaxMs = D(r, 3),
            CpuTotalMs = D(r, 4),
            LogicalReads = L(r, 5)
        }, ct);
    }
    private Task<List<QueryStat>> GetAdHocStatsAsync(CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (100)
                   qs.execution_count,
                   qs.total_elapsed_time / 1000.0 / qs.execution_count AS avg_ms,
                   qs.max_elapsed_time / 1000.0 AS max_ms,
                   qs.total_worker_time / 1000.0 AS cpu_ms,
                   qs.total_logical_reads,
                   SUBSTRING(st.text, (qs.statement_start_offset / 2) + 1,
                       ((CASE qs.statement_end_offset WHEN -1 THEN DATALENGTH(st.text) ELSE qs.statement_end_offset END
                         - qs.statement_start_offset) / 2) + 1) AS statement_text
            FROM sys.dm_exec_query_stats qs
            CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st
            CROSS APPLY (SELECT CONVERT(int, value) AS dbid
                         FROM sys.dm_exec_plan_attributes(qs.plan_handle)
                         WHERE attribute = 'dbid') pa
            WHERE pa.dbid = DB_ID() AND st.objectid IS NULL AND qs.execution_count > 0
              AND st.text NOT LIKE '%/* QueryCare */%'
            ORDER BY qs.total_elapsed_time / qs.execution_count DESC;
            """;
        return QueryAsync(sql, r =>
        {
            var text = S(r, 5);
            return new QueryStat
            {
                IsProcedure = false,
                Name = Fmt.OneLine(text, 70),
                Text = text,
                Executions = L(r, 0),
                AvgMs = D(r, 1),
                MaxMs = D(r, 2),
                CpuTotalMs = D(r, 3),
                LogicalReads = L(r, 4)
            };
        }, ct);
    }
    private async Task<(int Procedures, int AdHoc)> GetCountsAsync(CancellationToken ct)
    {
        const string sql = """
            SELECT (SELECT COUNT(*) FROM sys.dm_exec_procedure_stats WHERE database_id = DB_ID()),
                   (SELECT COUNT(*)
                    FROM sys.dm_exec_query_stats qs
                    CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st
                    CROSS APPLY (SELECT CONVERT(int, value) AS dbid
                                 FROM sys.dm_exec_plan_attributes(qs.plan_handle)
                                 WHERE attribute = 'dbid') pa
                    WHERE pa.dbid = DB_ID() AND st.objectid IS NULL
                      AND st.text NOT LIKE '%/* QueryCare */%');
            """;
        var rows = await QueryAsync(sql, r => (I(r, 0), I(r, 1)), ct);
        return rows[0];
    }

    private Task<List<MissingIndex>> GetMissingIndexesAsync(CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (50)
                   OBJECT_SCHEMA_NAME(d.object_id, d.database_id) + '.' + OBJECT_NAME(d.object_id, d.database_id) AS tbl,
                   d.equality_columns, d.inequality_columns, d.included_columns,
                   gs.user_seeks, gs.user_scans, gs.avg_total_user_cost, gs.avg_user_impact
            FROM sys.dm_db_missing_index_details d
            JOIN sys.dm_db_missing_index_groups g ON d.index_handle = g.index_handle
            JOIN sys.dm_db_missing_index_group_stats gs ON g.index_group_handle = gs.group_handle
            WHERE d.database_id = DB_ID()
            ORDER BY gs.user_seeks * gs.avg_total_user_cost * (gs.avg_user_impact / 100.0) DESC;
            """;
        return QueryAsync(sql, r => new MissingIndex
        {
            Table = S(r, 0),
            EqualityColumns = N(r, 1),
            InequalityColumns = N(r, 2),
            IncludedColumns = N(r, 3),
            Seeks = L(r, 4),
            Scans = L(r, 5),
            AvgCost = D(r, 6),
            Impact = D(r, 7)
        }, ct);
    }
    private async Task<List<T>> QueryAsync<T>(string sql, Func<SqlDataReader, T> map, CancellationToken ct)
    {
        var list = new List<T>();
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync(ct);
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = Preamble + sql;
        cmd.CommandTimeout = 30;
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct)) list.Add(map(rd));
        return list;
    }

    private static string S(SqlDataReader r, int i) => r.IsDBNull(i) ? "" : Convert.ToString(r.GetValue(i))!;
    private static string? N(SqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i));
    private static int I(SqlDataReader r, int i) => r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i));
    private static long L(SqlDataReader r, int i) => r.IsDBNull(i) ? 0 : Convert.ToInt64(r.GetValue(i));
    private static double D(SqlDataReader r, int i) => r.IsDBNull(i) ? 0 : Convert.ToDouble(r.GetValue(i));
}

