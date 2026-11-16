using Microsoft.Data.Sqlite;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Filtering.Sqlite.Extensions;

namespace VpnHood.Core.Filtering.Sqlite;

// Prepared raw insert statements per domain set; the target set is the action (include/exclude/block).
// Statements are prepared lazily — most builds populate a single set. Raw SQLitePCL statements for the
// same reason as SplitIpDbInserter: block lists can reach hundreds of thousands of rows. Created by
// SplitDomainDbBuilder on the build connection's handle, so the rows join the bulk-insert transaction.
/// <summary>
/// Inserts domain names into a split domain database during its build.
/// </summary>
/// <remarks>
/// <see cref="SplitDomainDbBuilder"/> makes one for its <c>InsertDomainsAsync</c>; the rows join the build's
/// transaction. It checks the build's cancellation every 20,000 rows.
/// </remarks>
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

    /// <summary>Inserts a domain name into a set.</summary>
    /// <param name="action">The set: <see cref="FilterAction.Include"/>, <see cref="FilterAction.Exclude"/> or <see cref="FilterAction.Block"/>.</param>
    /// <param name="invertedDomain">
    /// The name, as <see cref="DomainUtils.NormalizeDomain"/> gives it, with its labels reversed by
    /// <see cref="DomainUtils.InvertDomain"/>: <c>com.example</c> for <c>example.com</c>.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="action"/> is <see cref="FilterAction.Default"/>.</exception>
    /// <exception cref="SqliteException">SQLite refused the row.</exception>
    /// <exception cref="OperationCanceledException">The build's cancellation was requested.</exception>
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

    /// <summary>Releases the inserter's statements.</summary>
    public void Dispose()
    {
        foreach (var statement in _statements)
            statement?.Dispose();
    }
}
