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

    internal SplitIpDbInserter(SqliteConnection connection, CancellationToken cancellationToken)
    {
        // the raw handle stays an implementation detail of this hot loop; nothing above this layer sees it
        _db = connection.GetRequiredHandle();
        _cancellationToken = cancellationToken;
    }

    // start/end are raw big-endian address bytes (4 for IPv4, 16 for IPv6), as serialized in the assets
    public void Insert(FilterAction action, byte[] start, byte[] end)
    {
        if (start.Length == 4) {
            var statement = GetStatement(action, isV4: true);
            SQLitePCL.raw.sqlite3_bind_int64(statement, 1, SplitIpDb.ToV4Key(start));
            SQLitePCL.raw.sqlite3_bind_int64(statement, 2, SplitIpDb.ToV4Key(end));
            SplitDbRaw.StepReset(_db, statement);
        }
        else {
            var statement = GetStatement(action, isV4: false);
            SQLitePCL.raw.sqlite3_bind_blob(statement, 1, start);
            SQLitePCL.raw.sqlite3_bind_blob(statement, 2, end);
            SplitDbRaw.StepReset(_db, statement);
        }

        if (++_rowCount % CancellationCheckInterval == 0)
            _cancellationToken.ThrowIfCancellationRequested();
    }

    private SQLitePCL.sqlite3_stmt GetStatement(FilterAction action, bool isV4)
    {
        var index = ((int)action << 1) | (isV4 ? 1 : 0);
        return _statements[index] ??= SplitDbRaw.PrepareRaw(_db,
            $"INSERT INTO {SplitIpDb.GetTableName(action, isV4)} (start_ip, end_ip) VALUES (?, ?)");
    }

    public void Dispose()
    {
        foreach (var statement in _statements)
            statement?.Dispose();
    }
}
