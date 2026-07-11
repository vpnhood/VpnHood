using Microsoft.Data.Sqlite;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Filtering.Sqlite;

// Split-domain flavor of the shared build core: binds the domain schema and hands derived classes a
// domain inserter. Derived classes supply only the context's business: what identifies the source
// (BuildSourceSignature) and how to stream its domains (InsertDomainsAsync).
public abstract class SplitDomainDbBuilder : SplitDbBuilder
{
    // Stream the context's domains into the db. Invoked only on the rebuild path.
    protected abstract Task InsertDomainsAsync(SplitDomainDbInserter inserter, CancellationToken cancellationToken);

    protected sealed override int SchemaVersion => SplitDomainDb.SchemaVersion;
    protected sealed override string CreateTablesSql => SplitDomainDb.CreateTablesSql;
    protected sealed override string CreateIndexesSql => SplitDomainDb.CreateIndexesSql;

    protected sealed override async Task InsertAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        // the inserter's raw statements run on the connection's handle, so the rows join the surrounding
        // transaction; scoped to this method so they are finalized before the connection closes
        using var inserter = new SplitDomainDbInserter(connection, cancellationToken);
        await InsertDomainsAsync(inserter, cancellationToken).Vhc();
    }
}
