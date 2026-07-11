using Microsoft.Data.Sqlite;
using VpnHood.Core.Filtering.Abstractions;

namespace VpnHood.Core.Filtering.Sqlite;

// Prepared raw insert statements per (set, address family); the target set is the action (include/exclude/
// block) and the family is dispatched by address length (4 ⇒ v4, else v6). Statements are prepared lazily —
// most builds populate a single set. The hot loop uses raw SQLitePCL statements (prepare once,
// bind/step/reset per row): the SqliteCommand path costs ~30µs/row on Android (Mono) vs ~1µs/row raw —
// measured on an emulator, an all-countries build (618k rows) dropped from 16.3s to 0.65s. Created by
// SplitIpDbBuilder on the build connection's handle, so the rows join the surrounding bulk-insert transaction.
public sealed class SplitIpDbInserter : IDisposable
{
    private const int CancellationCheckInterval = 20000;

    private readonly SQLitePCL.sqlite3 _db;
    private readonly CancellationToken _cancellationToken;
    private readonly SQLitePCL.sqlite3_stmt?[] _statements = new SQLitePCL.sqlite3_stmt?[8];
    private int _rowCount;

    internal SplitIpDbInserter(SQLitePCL.sqlite3 db, CancellationToken cancellationToken)
    {
        _db = db;
        _cancellationToken = cancellationToken;
    }

    // start/end are raw big-endian address bytes (4 for IPv4, 16 for IPv6), as serialized in the assets
    public void Insert(FilterAction action, byte[] start, byte[] end)
    {
        if (start.Length == 4) {
            var statement = GetStatement(action, isV4: true);
            SQLitePCL.raw.sqlite3_bind_int64(statement, 1, SplitIpDb.ToV4Key(start));
            SQLitePCL.raw.sqlite3_bind_int64(statement, 2, SplitIpDb.ToV4Key(end));
            StepReset(_db, statement);
        }
        else {
            var statement = GetStatement(action, isV4: false);
            SQLitePCL.raw.sqlite3_bind_blob(statement, 1, start);
            SQLitePCL.raw.sqlite3_bind_blob(statement, 2, end);
            StepReset(_db, statement);
        }

        if (++_rowCount % CancellationCheckInterval == 0)
            _cancellationToken.ThrowIfCancellationRequested();
    }

    private SQLitePCL.sqlite3_stmt GetStatement(FilterAction action, bool isV4)
    {
        var index = ((int)action << 1) | (isV4 ? 1 : 0);
        return _statements[index] ??= PrepareRaw(_db,
            $"INSERT INTO {SplitIpDb.GetTableName(action, isV4)} (start_ip, end_ip) VALUES (?, ?)");
    }

    public void Dispose()
    {
        foreach (var statement in _statements)
            statement?.Dispose();
    }

    private static SQLitePCL.sqlite3_stmt PrepareRaw(SQLitePCL.sqlite3 db, string sql)
    {
        CheckRc(db, SQLitePCL.raw.sqlite3_prepare_v2(db, sql, out var statement));
        return statement;
    }

    private static void StepReset(SQLitePCL.sqlite3 db, SQLitePCL.sqlite3_stmt statement)
    {
        CheckRc(db, SQLitePCL.raw.sqlite3_step(statement));
        SQLitePCL.raw.sqlite3_reset(statement);
    }

    private static void CheckRc(SQLitePCL.sqlite3 db, int rc)
    {
        if (rc is SQLitePCL.raw.SQLITE_OK or SQLitePCL.raw.SQLITE_DONE or SQLitePCL.raw.SQLITE_ROW)
            return;
        throw new SqliteException($"SQLite error {rc}: {SQLitePCL.raw.sqlite3_errmsg(db).utf8_to_string()}", rc);
    }
}
