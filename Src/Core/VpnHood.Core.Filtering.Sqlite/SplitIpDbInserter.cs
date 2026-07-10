using Microsoft.Data.Sqlite;

namespace VpnHood.Core.Filtering.Sqlite;

// Prepared raw insert statements for both address families; dispatches by address length (4 ⇒ v4, else v6).
// The hot loop uses raw SQLitePCL statements (prepare once, bind/step/reset per row): the SqliteCommand path
// costs ~30µs/row on Android (Mono) vs ~1µs/row raw — measured on an emulator, an all-countries build
// (618k rows) dropped from 16.3s to 0.65s. Created by SplitIpDbBuilder on the build connection's handle,
// so the rows join the surrounding bulk-insert transaction.
public sealed class SplitIpDbInserter : IDisposable
{
    private const int CancellationCheckInterval = 20000;

    private readonly SQLitePCL.sqlite3 _db;
    private readonly CancellationToken _cancellationToken;
    private readonly SQLitePCL.sqlite3_stmt _insertV4;
    private readonly SQLitePCL.sqlite3_stmt _insertV6;
    private int _rowCount;

    internal SplitIpDbInserter(SQLitePCL.sqlite3 db, CancellationToken cancellationToken)
    {
        _db = db;
        _cancellationToken = cancellationToken;
        _insertV4 = PrepareRaw(db, "INSERT INTO range_v4 (start_ip, end_ip) VALUES (?, ?)");
        _insertV6 = PrepareRaw(db, "INSERT INTO range_v6 (start_ip, end_ip) VALUES (?, ?)");
    }

    // start/end are raw big-endian address bytes (4 for IPv4, 16 for IPv6), as serialized in the assets
    public void Insert(byte[] start, byte[] end)
    {
        if (start.Length == 4) {
            SQLitePCL.raw.sqlite3_bind_int64(_insertV4, 1, SplitIpDb.ToV4Key(start));
            SQLitePCL.raw.sqlite3_bind_int64(_insertV4, 2, SplitIpDb.ToV4Key(end));
            StepReset(_db, _insertV4);
        }
        else {
            SQLitePCL.raw.sqlite3_bind_blob(_insertV6, 1, start);
            SQLitePCL.raw.sqlite3_bind_blob(_insertV6, 2, end);
            StepReset(_db, _insertV6);
        }

        if (++_rowCount % CancellationCheckInterval == 0)
            _cancellationToken.ThrowIfCancellationRequested();
    }

    public void Dispose()
    {
        _insertV4.Dispose();
        _insertV6.Dispose();
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
