using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Generics;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Proxies.Management.Sqlite;

/// <summary>
/// SQLite-backed shared proxy endpoint store. The db file lives in a folder both the app and the
/// VPN service process can access (on iOS the app-group container). Connections are opened per
/// operation with pooling disabled so no file lock survives between operations — a suspended iOS
/// process holding a lock on an app-group file is killed with 0xdead10cc.
/// </summary>
public class ProxyEndPointStore(string dbPath) : IProxyEndPointStore
{
    private const int SchemaVersion = 1;
    private readonly AsyncLock _lock = new(); // not reentrant; public methods must not call each other
    private bool _schemaVerified;

    // WAL gives cross-process reader/writer concurrency; journal_mode persists in the db file.
    // Isolated here so it can be switched to TRUNCATE if app-group WAL misbehaves on iOS devices.
    private const string JournalMode = "WAL";

    // Open the per-operation connection. Callers must hold _lock. The first call also creates the
    // folder and verifies/creates the schema (see EnsureDb).
    private async Task<SqliteConnection> OpenDb()
    {
        await EnsureDb().Vhc();
        return await OpenConnection().Vhc();
    }

    private async Task<SqliteConnection> OpenConnection()
    {
        SqliteHelper.EnsureInitialized();
        var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False;Default Timeout=5");
        await connection.OpenAsync().Vhc();
        await SqliteHelper.ExecuteAsync(connection, "PRAGMA busy_timeout=5000;").Vhc();
        return connection;
    }

    // one-time (per store instance): create the folder and verify/create the schema; an unusable
    // or foreign db is deleted and recreated empty (legacy data is disposable). Callers hold _lock.
    private async Task EnsureDb()
    {
        if (_schemaVerified)
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        try {
            await using var connection = await OpenConnection().Vhc();
            await EnsureSchema(connection).Vhc();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex,
                "Could not open the proxy endpoint db. Recreating... DbPath: {DbPath}", dbPath);
            Recreate();

            await using var connection = await OpenConnection().Vhc();
            await EnsureSchema(connection).Vhc();
        }

        _schemaVerified = true;
    }

    private void Recreate()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" }) {
            var file = dbPath + suffix;
            if (File.Exists(file))
                File.Delete(file);
        }
    }

    private static async Task EnsureSchema(SqliteConnection connection)
    {
        await SqliteHelper.ExecuteAsync(connection, $"PRAGMA journal_mode={JournalMode};").Vhc();

        // verify schema version; an empty db has no meta table yet
        var hasMeta = await SqliteHelper.ExecuteScalarAsync(connection,
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='meta'").Vhc() is long and > 0;
        if (hasMeta) {
            var version = await SqliteHelper.ExecuteScalarAsync(connection,
                "SELECT value FROM meta WHERE key='schema_version'").Vhc() as string;
            if (version == SchemaVersion.ToString())
                return; // schema is current
            throw new SqliteException($"Unsupported proxy endpoint db schema version: {version}.", 26 /*SQLITE_NOTADB*/);
        }

        await SqliteHelper.ExecuteAsync(connection,
            """
            CREATE TABLE IF NOT EXISTS endpoints (
                id              TEXT PRIMARY KEY,
                protocol        INTEGER NOT NULL,
                host            TEXT    NOT NULL,
                port            INTEGER NOT NULL,
                username        TEXT    NULL,
                password        TEXT    NULL,
                is_enabled      INTEGER NOT NULL DEFAULT 1,
                country_code    TEXT    NULL,
                penalty         INTEGER NOT NULL DEFAULT 0,
                succeeded_count INTEGER NOT NULL DEFAULT 0,
                failed_count    INTEGER NOT NULL DEFAULT 0,
                latency_ms      INTEGER NULL,
                last_succeeded  INTEGER NULL,
                last_failed     INTEGER NULL,
                error_message   TEXT    NULL,
                queue_position  INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            """).Vhc();
        await SqliteHelper.ExecuteAsync(connection,
            "INSERT OR REPLACE INTO meta (key, value) VALUES ('schema_version', @version)",
            ("@version", SchemaVersion.ToString())).Vhc();
    }

    public async Task<ProxyEndPointRecord?> Get(string id)
    {
        using var scope = await _lock.LockAsync().Vhc();
        await using var connection = await OpenDb().Vhc();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM endpoints WHERE id=@id";
        command.Parameters.AddWithValue("@id", id);
        await using var reader = await command.ExecuteReaderAsync().Vhc();
        return await reader.ReadAsync().Vhc() ? ReadRecord(reader) : null;
    }

    public async Task<int> Count()
    {
        using var scope = await _lock.LockAsync().Vhc();
        await using var connection = await OpenDb().Vhc();
        return Convert.ToInt32(await SqliteHelper.ExecuteScalarAsync(connection, "SELECT COUNT(*) FROM endpoints").Vhc());
    }

    // mirrors ProxyEndPointStatus.Quality so ordering happens in SQL
    private const string QualitySql =
        """
        CASE
            WHEN penalty <= 0 AND succeeded_count = 0 AND failed_count = 0 THEN 0
            WHEN penalty <= 0 AND succeeded_count > 0 THEN 1
            WHEN penalty <= 10 AND succeeded_count > 0 THEN 2
            WHEN penalty <= 20 AND succeeded_count > 0 THEN 3
            WHEN penalty <= 100 AND succeeded_count > 0 THEN 4
            WHEN penalty <= 10000 AND succeeded_count > 0 THEN 5
            ELSE 6
        END
        """;

    public async Task<IReadOnlyList<ProxyEndPointRecord>> List()
    {
        using var scope = await _lock.LockAsync().Vhc();
        await using var connection = await OpenDb().Vhc();
        return await ReadAll(connection).Vhc();
    }

    // shared by List/Merge/DeleteAll; the caller holds _lock and owns the connection
    private static async Task<List<ProxyEndPointRecord>> ReadAll(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM endpoints";
        await using var reader = await command.ExecuteReaderAsync().Vhc();
        var records = new List<ProxyEndPointRecord>();
        while (await reader.ReadAsync().Vhc())
            records.Add(ReadRecord(reader));
        return records;
    }

    public async Task<ListResult<ProxyEndPointRecord>> List(ProxyEndPointStoreListParams options)
    {
        // build the WHERE clause; filtering, ordering and paging all run inside SQLite so only
        // the requested page is materialized
        var conditions = new List<string>();
        var parameters = new List<(string Name, object Value)>();

        if (!options.IncludeDisabled)
            conditions.Add("is_enabled = 1");

        if (options.Search != null) {
            conditions.Add(@"(host LIKE @search ESCAPE '\' OR country_code LIKE @search ESCAPE '\')");
            var escaped = options.Search
                .Replace(@"\", @"\\")
                .Replace("%", @"\%")
                .Replace("_", @"\_");
            parameters.Add(("@search", $"%{escaped}%"));
        }

        // category filters mirror IsLastUsedSucceeded / IsLastUsedFailed / !HasUsed
        const string succeededSql =
            "(last_succeeded IS NOT NULL AND (last_failed IS NULL OR last_succeeded > last_failed))";
        const string failedSql =
            "(last_failed IS NOT NULL AND (last_succeeded IS NULL OR last_failed > last_succeeded))";
        const string unusedSql = "(succeeded_count = 0 AND failed_count = 0)";

        var categories = new List<string>();
        if (options.IncludeSucceeded)
            categories.Add(succeededSql);
        if (options.IncludeFailed)
            categories.Add(failedSql);
        if (options.IncludeUnknown)
            categories.Add(unusedSql);
        conditions.Add(categories.Count > 0 ? $"({string.Join(" OR ", categories)})" : "0");

        var where = string.Join(" AND ", conditions);

        using var scope = await _lock.LockAsync().Vhc();
        await using var connection = await OpenDb().Vhc();

        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = $"SELECT COUNT(*) FROM endpoints WHERE {where}";
        foreach (var (name, value) in parameters)
            countCommand.Parameters.AddWithValue(name, value);
        var totalCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync().Vhc());

        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
             SELECT * FROM endpoints WHERE {where}
             ORDER BY is_enabled DESC, {QualitySql}, rowid
             LIMIT @recordCount OFFSET @recordIndex
             """;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        command.Parameters.AddWithValue("@recordCount", options.RecordCount);
        command.Parameters.AddWithValue("@recordIndex", options.RecordIndex);

        await using var reader = await command.ExecuteReaderAsync().Vhc();
        var records = new List<ProxyEndPointRecord>();
        while (await reader.ReadAsync().Vhc())
            records.Add(ReadRecord(reader));

        return new ListResult<ProxyEndPointRecord> {
            Items = records,
            TotalCount = totalCount
        };
    }

    public async Task Upsert(IReadOnlyList<ProxyEndPointRecord> records, bool keepExistingStatus = true,
        bool keepExistingEnabled = false)
    {
        if (records.Count == 0)
            return;

        using var scope = await _lock.LockAsync().Vhc();
        await using var connection = await OpenDb().Vhc();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync().Vhc();
        foreach (var record in records)
            await UpsertCore(connection, transaction, record, keepExistingStatus, keepExistingEnabled).Vhc();
        await transaction.CommitAsync().Vhc();
    }

    private static async Task UpsertCore(SqliteConnection connection, SqliteTransaction transaction,
        ProxyEndPointRecord record, bool keepExistingStatus, bool keepExistingEnabled)
    {
        var statusUpdate = keepExistingStatus
            ? ""
            : """
              , penalty=excluded.penalty, succeeded_count=excluded.succeeded_count,
              failed_count=excluded.failed_count, latency_ms=excluded.latency_ms,
              last_succeeded=excluded.last_succeeded, last_failed=excluded.last_failed,
              error_message=excluded.error_message, queue_position=excluded.queue_position
              """;

        var enabledUpdate = keepExistingEnabled ? "" : ", is_enabled=excluded.is_enabled";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
             INSERT INTO endpoints (id, protocol, host, port, username, password, is_enabled, country_code,
                 penalty, succeeded_count, failed_count, latency_ms, last_succeeded, last_failed,
                 error_message, queue_position)
             VALUES (@id, @protocol, @host, @port, @username, @password, @isEnabled, @countryCode,
                 @penalty, @succeededCount, @failedCount, @latencyMs, @lastSucceeded, @lastFailed,
                 @errorMessage, @queuePosition)
             ON CONFLICT(id) DO UPDATE SET
                 protocol=excluded.protocol, host=excluded.host, port=excluded.port,
                 username=excluded.username, password=excluded.password,
                 country_code=COALESCE(excluded.country_code, country_code)
                 {enabledUpdate}{statusUpdate}
             """;

        var endPoint = record.EndPoint;
        var status = record.Status;
        command.Parameters.AddWithValue("@id", endPoint.Id);
        command.Parameters.AddWithValue("@protocol", (int)endPoint.Protocol);
        command.Parameters.AddWithValue("@host", endPoint.Host);
        command.Parameters.AddWithValue("@port", endPoint.Port);
        command.Parameters.AddWithValue("@username", (object?)endPoint.Username ?? DBNull.Value);
        command.Parameters.AddWithValue("@password", (object?)endPoint.Password ?? DBNull.Value);
        command.Parameters.AddWithValue("@isEnabled", endPoint.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("@countryCode", (object?)record.CountryCode ?? DBNull.Value);
        AddStatusParameters(command, status);
        await command.ExecuteNonQueryAsync().Vhc();
    }

    public async Task UpdateStatuses(IReadOnlyList<ProxyEndPointInfo> infos)
    {
        if (infos.Count == 0)
            return;

        using var scope = await _lock.LockAsync().Vhc();
        await using var connection = await OpenDb().Vhc();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync().Vhc();
        foreach (var info in infos) {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                UPDATE endpoints SET
                    is_enabled=@isEnabled, penalty=@penalty, succeeded_count=@succeededCount,
                    failed_count=@failedCount, latency_ms=@latencyMs, last_succeeded=@lastSucceeded,
                    last_failed=@lastFailed, error_message=@errorMessage, queue_position=@queuePosition
                WHERE id=@id
                """;
            command.Parameters.AddWithValue("@id", info.EndPoint.Id);
            command.Parameters.AddWithValue("@isEnabled", info.EndPoint.IsEnabled ? 1 : 0);
            AddStatusParameters(command, info.Status);
            await command.ExecuteNonQueryAsync().Vhc();
        }

        await transaction.CommitAsync().Vhc();
    }

    public async Task Delete(IReadOnlyList<string> ids)
    {
        if (ids.Count == 0)
            return;

        using var scope = await _lock.LockAsync().Vhc();
        await using var connection = await OpenDb().Vhc();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync().Vhc();
        await DeleteCore(connection, transaction, ids).Vhc();
        await transaction.CommitAsync().Vhc();
    }

    // shared by Delete/DeleteAll/Merge; the caller holds _lock and owns the transaction
    private static async Task DeleteCore(SqliteConnection connection, SqliteTransaction transaction,
        IReadOnlyList<string> ids)
    {
        foreach (var id in ids) {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM endpoints WHERE id=@id";
            command.Parameters.AddWithValue("@id", id);
            await command.ExecuteNonQueryAsync().Vhc();
        }
    }

    public async Task DeleteAll(DeleteAllOptions options)
    {
        using var scope = await _lock.LockAsync().Vhc();
        await using var connection = await OpenDb().Vhc();

        // keep the exact category semantics of the old app-side filter chain
        var allRecords = await ReadAll(connection).Vhc();
        var keep = allRecords.AsEnumerable();

        if (options.DeleteSucceeded)
            keep = keep.Where(x => x.Status is not { HasUsed: true, IsLastUsedSucceeded: true });

        if (options.DeleteFailed)
            keep = keep.Where(x => x.Status is not { HasUsed: true, IsLastUsedFailed: true });

        if (options.DeleteUnknown)
            keep = keep.Where(x => x.Status.HasUsed);

        if (options.DeleteDisabled)
            keep = keep.Where(x => x.EndPoint.IsEnabled);

        var keepIds = keep.Select(x => x.EndPoint.Id).ToHashSet();
        var deleteIds = allRecords
            .Select(x => x.EndPoint.Id)
            .Where(id => !keepIds.Contains(id))
            .ToArray();

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync().Vhc();
        await DeleteCore(connection, transaction, deleteIds).Vhc();
        await transaction.CommitAsync().Vhc();
    }

    public async Task DisableAllFailed()
    {
        using var scope = await _lock.LockAsync().Vhc();
        await using var connection = await OpenDb().Vhc();

        // HasUsed && !IsLastUsedSucceeded
        await SqliteHelper.ExecuteAsync(connection,
            """
            UPDATE endpoints SET is_enabled=0
            WHERE (succeeded_count > 0 OR failed_count > 0)
              AND (last_succeeded IS NULL OR (last_failed IS NOT NULL AND last_succeeded <= last_failed))
            """).Vhc();
    }

    public async Task SetCountryCode(string id, string? countryCode)
    {
        using var scope = await _lock.LockAsync().Vhc();
        await using var connection = await OpenDb().Vhc();
        await SqliteHelper.ExecuteAsync(connection,
            "UPDATE endpoints SET country_code=@countryCode WHERE id=@id",
            ("@countryCode", (object?)countryCode ?? DBNull.Value), ("@id", id)).Vhc();
    }

    public async Task ResetStatuses()
    {
        using var scope = await _lock.LockAsync().Vhc();
        await using var connection = await OpenDb().Vhc();
        await SqliteHelper.ExecuteAsync(connection,
            """
            UPDATE endpoints SET penalty=0, succeeded_count=0, failed_count=0, latency_ms=NULL,
                last_succeeded=NULL, last_failed=NULL, error_message=NULL, queue_position=0
            """).Vhc();
        await SqliteHelper.ExecuteAsync(connection,
            "INSERT OR REPLACE INTO meta (key, value) VALUES ('queue_position', '0')").Vhc();
    }

    public async Task Merge(IReadOnlyList<ProxyEndPoint> newEndPoints, int? maxItemCount, int? maxPenalty,
        bool removeDuplicateIps)
    {
        using var scope = await _lock.LockAsync().Vhc();
        await using var connection = await OpenDb().Vhc();

        // the standard priority merge works on the full current list; ~1000 rows max
        var currentRecords = await ReadAll(connection).Vhc();
        var currentInfos = currentRecords.Select(x => x.ToInfo()).ToArray();
        var mergedEndPoints = ProxyEndPointUpdater.Merge(
            currentInfos, newEndPoints as ProxyEndPoint[] ?? newEndPoints.ToArray(),
            maxItemCount, maxPenalty, removeDuplicateIps);

        var mergedIds = mergedEndPoints.Select(x => x.Id).ToHashSet();
        var prunedIds = currentRecords
            .Select(x => x.EndPoint.Id)
            .Where(id => !mergedIds.Contains(id))
            .ToArray();

        var records = mergedEndPoints
            .Select(x => new ProxyEndPointRecord { EndPoint = x })
            .ToArray();

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync().Vhc();
        await DeleteCore(connection, transaction, prunedIds).Vhc();

        // existing rows keep their status and enabled state; new rows start fresh
        foreach (var record in records)
            await UpsertCore(connection, transaction, record, keepExistingStatus: true, keepExistingEnabled: true).Vhc();

        await transaction.CommitAsync().Vhc();
    }

    public async Task<long> GetQueuePosition()
    {
        using var scope = await _lock.LockAsync().Vhc();
        await using var connection = await OpenDb().Vhc();
        return await SqliteHelper.ExecuteScalarAsync(connection,
            "SELECT value FROM meta WHERE key='queue_position'").Vhc() is string value
            ? long.Parse(value)
            : 0L;
    }

    public async Task SetQueuePosition(long queuePosition)
    {
        using var scope = await _lock.LockAsync().Vhc();
        await using var connection = await OpenDb().Vhc();
        await SqliteHelper.ExecuteAsync(connection,
            "INSERT OR REPLACE INTO meta (key, value) VALUES ('queue_position', @value)",
            ("@value", queuePosition.ToString())).Vhc();
    }

    private static void AddStatusParameters(SqliteCommand command, ProxyEndPointStatus status)
    {
        command.Parameters.AddWithValue("@penalty", status.Penalty);
        command.Parameters.AddWithValue("@succeededCount", status.SucceededCount);
        command.Parameters.AddWithValue("@failedCount", status.FailedCount);
        command.Parameters.AddWithValue("@latencyMs",
            status.Latency != null ? (long)status.Latency.Value.TotalMilliseconds : DBNull.Value);
        command.Parameters.AddWithValue("@lastSucceeded", SqliteHelper.ToUnixMs(status.LastSucceeded));
        command.Parameters.AddWithValue("@lastFailed", SqliteHelper.ToUnixMs(status.LastFailed));
        command.Parameters.AddWithValue("@errorMessage", (object?)status.ErrorMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("@queuePosition", status.QueuePosition);
    }

    private static ProxyEndPointRecord ReadRecord(SqliteDataReader reader)
    {
        return new ProxyEndPointRecord {
            EndPoint = new ProxyEndPoint {
                Protocol = (ProxyProtocol)reader.GetInt32(reader.GetOrdinal("protocol")),
                Host = reader.GetString(reader.GetOrdinal("host")),
                Port = reader.GetInt32(reader.GetOrdinal("port")),
                Username = SqliteHelper.GetNullableString(reader, "username"),
                Password = SqliteHelper.GetNullableString(reader, "password"),
                IsEnabled = reader.GetInt32(reader.GetOrdinal("is_enabled")) != 0
            },
            CountryCode = SqliteHelper.GetNullableString(reader, "country_code"),
            Status = new ProxyEndPointStatus {
                Penalty = reader.GetInt32(reader.GetOrdinal("penalty")),
                SucceededCount = reader.GetInt32(reader.GetOrdinal("succeeded_count")),
                FailedCount = reader.GetInt32(reader.GetOrdinal("failed_count")),
                Latency = SqliteHelper.GetNullableInt64(reader, "latency_ms") is { } latencyMs
                    ? TimeSpan.FromMilliseconds(latencyMs)
                    : null,
                LastSucceeded = SqliteHelper.FromUnixMs(SqliteHelper.GetNullableInt64(reader, "last_succeeded")),
                LastFailed = SqliteHelper.FromUnixMs(SqliteHelper.GetNullableInt64(reader, "last_failed")),
                ErrorMessage = SqliteHelper.GetNullableString(reader, "error_message"),
                QueuePosition = reader.GetInt64(reader.GetOrdinal("queue_position"))
            }
        };
    }

    public void Dispose()
    {
        // connections are per-operation; nothing is held open
    }
}
