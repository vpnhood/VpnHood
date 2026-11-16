using Microsoft.Data.Sqlite;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Core.Filtering.Sqlite;

// Split-domain flavor of the shared build core: binds the domain schema and hands derived classes a
// domain inserter. Derived classes supply only the context's business: what identifies the source
// (GetSourceSignature) and how to stream its domains (InsertDomainsAsync).
/// <summary>
/// Provides the build of a split domain database: domain names in include, exclude and block sets, which
/// <see cref="SqliteDomainFilter"/> reads.
/// </summary>
/// <remarks>A derived class gives the source's signature and its names.</remarks>
public abstract class SplitDomainDbBuilder : SplitDbBuilder
{
    /// <summary>
    /// Writes the source's domain names through an inserter, during a build.
    /// </summary>
    /// <param name="inserter">The inserter, which adds each name to the build's transaction.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <remarks>It is called only when a build runs.</remarks>
    protected abstract Task InsertDomainsAsync(SplitDomainDbInserter inserter, CancellationToken cancellationToken);

    private protected sealed override int SchemaVersion => SplitDomainDb.SchemaVersion;
    private protected sealed override string CreateTablesSql => SplitDomainDb.CreateTablesSql;
    private protected sealed override string CreateIndexesSql => SplitDomainDb.CreateIndexesSql;

    private protected sealed override async Task InsertAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        // the inserter's raw statements run on the connection's handle, so the rows join the surrounding
        // transaction; scoped to this method so they are finalized before the connection closes
        using var inserter = new SplitDomainDbInserter(connection, cancellationToken);
        await InsertDomainsAsync(inserter, cancellationToken).Vhc();
    }
}
