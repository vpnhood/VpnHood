using Microsoft.Data.Sqlite;
using VpnHood.Core.Toolkit.Extensions;

namespace VpnHood.Core.Filtering.Sqlite;

// Split-ip flavor of the shared build core: binds the ip schema and hands derived classes a range
// inserter. Derived classes supply only the context's business: what identifies the source
// (GetSourceSignature) and how to stream its ranges (InsertRangesAsync).
public abstract class SplitIpDbBuilder : SplitDbBuilder
{
    // Stream the context's ranges into the db. Invoked only on the rebuild path.
    protected abstract Task InsertRangesAsync(SplitIpDbInserter inserter, CancellationToken cancellationToken);

    protected sealed override int SchemaVersion => SplitIpDb.SchemaVersion;
    protected sealed override string CreateTablesSql => SplitIpDb.CreateTablesSql;
    protected sealed override string CreateIndexesSql => SplitIpDb.CreateIndexesSql;

    protected sealed override async Task InsertAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        // the inserter's raw statements run on the connection's handle, so the rows join the surrounding
        // transaction; scoped to this method so they are finalized before the connection closes
        using var inserter = new SplitIpDbInserter(connection, cancellationToken);
        await InsertRangesAsync(inserter, cancellationToken).Vhc();
    }
}
