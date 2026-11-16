using Microsoft.Data.Sqlite;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Core.Filtering.Sqlite;

// Split-ip flavor of the shared build core: binds the ip schema and hands derived classes a range
// inserter. Derived classes supply only the context's business: what identifies the source
// (GetSourceSignature) and how to stream its ranges (InsertRangesAsync).
/// <summary>
/// Provides the build of a split IP database: IPv4 and IPv6 address ranges in include, exclude and block sets, which
/// <see cref="SqliteIpFilter"/> reads.
/// </summary>
/// <remarks>A derived class gives the source's signature and its ranges.</remarks>
public abstract class SplitIpDbBuilder : SplitDbBuilder
{
    /// <summary>
    /// Writes the source's ranges through an inserter, during a build.
    /// </summary>
    /// <param name="inserter">The inserter, which adds each range to the build's transaction.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <remarks>
    /// It is called only when a build runs. The ranges of a set must not overlap: a lookup checks only the range
    /// whose start is the greatest at or below the address.
    /// </remarks>
    protected abstract Task InsertRangesAsync(SplitIpDbInserter inserter, CancellationToken cancellationToken);

    private protected sealed override int SchemaVersion => SplitIpDb.SchemaVersion;
    private protected sealed override string CreateTablesSql => SplitIpDb.CreateTablesSql;
    private protected sealed override string CreateIndexesSql => SplitIpDb.CreateIndexesSql;

    private protected sealed override async Task InsertAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        // the inserter's raw statements run on the connection's handle, so the rows join the surrounding
        // transaction; scoped to this method so they are finalized before the connection closes
        using var inserter = new SplitIpDbInserter(connection, cancellationToken);
        await InsertRangesAsync(inserter, cancellationToken).Vhc();
    }
}
