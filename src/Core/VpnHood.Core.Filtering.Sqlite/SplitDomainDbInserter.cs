using Microsoft.Data.Sqlite;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Filtering.Sqlite.Extensions;

namespace VpnHood.Core.Filtering.Sqlite;

// Prepared raw insert statements per domain set; the target set is the action (include/exclude/block).
// Statements are prepared lazily — most builds populate a single set. Raw SQLitePCL statements for the
// same reason as SplitIpDbInserter: block lists can reach hundreds of thousands of rows. Created by
// SplitDomainDbBuilder on the build connection's handle, so the rows join the bulk-insert transaction.
public sealed class SplitDomainDbInserter : IDisposable
{
    private const int CancellationCheckInterval = 20000;

    private readonly SQLitePCL.sqlite3 _db;
    private readonly CancellationToken _cancellationToken;
    private readonly SQLitePCL.sqlite3_stmt?[] _statements = new SQLitePCL.sqlite3_stmt?[4];
    private int _rowCount;

    internal SplitDomainDbInserter(SqliteConnection connection, CancellationToken cancellationToken)
    {
        // the raw handle stays an implementation detail of this hot loop; nothing above this layer sees it
        _db = connection.GetRequiredHandle();
        _cancellationToken = cancellationToken;
    }

    // invertedDomain is the canonical DomainUtils form: normalized + part-inverted ("com.google")
    public void Insert(FilterAction action, string invertedDomain)
    {
        var statement = GetStatement(action);
        SQLitePCL.raw.sqlite3_bind_text(statement, 1, invertedDomain);
        SplitDbRaw.StepReset(_db, statement);

        if (++_rowCount % CancellationCheckInterval == 0)
            _cancellationToken.ThrowIfCancellationRequested();
    }

    private SQLitePCL.sqlite3_stmt GetStatement(FilterAction action)
    {
        return _statements[(int)action] ??= SplitDbRaw.PrepareRaw(_db,
            $"INSERT INTO {SplitDomainDb.GetTableName(action)} (domain) VALUES (?)");
    }

    public void Dispose()
    {
        foreach (var statement in _statements)
            statement?.Dispose();
    }
}
