using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Core.Filtering.Sqlite;

// Generic concrete builder: stores plain IpRangeOrderedLists into the db's per-action tables, exactly as
// given (no merge algebra — the db mirrors its sources). All inputs are factories, invoked lazily — the
// signature on every EnsureAsync (so keep it cheap: hashes/stat, never parse), the range sets only on the
// rare rebuild path. An omitted or empty set leaves its tables empty: no constraint for include, nothing
// to match for exclude/block.
/// <summary>
/// Provides the build of a split IP database from plain lists of ranges, stored as given.
/// </summary>
/// <param name="sourceSignatureFactory">Gives the source's signature (<see cref="SplitDbBuilder.GetSourceSignature"/>).</param>
/// <param name="includesFactory">Gives the include set, or <see langword="null"/> to leave it empty.</param>
/// <param name="excludesFactory">Gives the exclude set, or <see langword="null"/> to leave it empty.</param>
/// <param name="blocksFactory">Gives the block set, or <see langword="null"/> to leave it empty.</param>
/// <remarks>
/// The signature's factory runs at every <see cref="SplitDbBuilder.EnsureAsync"/>, so keep it cheap; the sets'
/// factories run only when a build runs.
/// </remarks>
public class IpRangeListDbBuilder(
    Func<string> sourceSignatureFactory,
    Func<IpRangeOrderedList>? includesFactory = null,
    Func<IpRangeOrderedList>? excludesFactory = null,
    Func<IpRangeOrderedList>? blocksFactory = null)
    : SplitIpDbBuilder
{
    /// <inheritdoc/>
    /// <returns>What the signature's factory gives.</returns>
    public override string GetSourceSignature() => sourceSignatureFactory();

    /// <inheritdoc/>
    /// <remarks>It inserts each given set's ranges.</remarks>
    /// <exception cref="Microsoft.Data.Sqlite.SqliteException">SQLite refused a row.</exception>
    /// <exception cref="OperationCanceledException">The build's cancellation was requested.</exception>
    protected override Task InsertRangesAsync(SplitIpDbInserter inserter, CancellationToken cancellationToken)
    {
        InsertSet(inserter, FilterAction.Include, includesFactory);
        InsertSet(inserter, FilterAction.Exclude, excludesFactory);
        InsertSet(inserter, FilterAction.Block, blocksFactory);
        return Task.CompletedTask;
    }

    private static void InsertSet(SplitIpDbInserter inserter, FilterAction action,
        Func<IpRangeOrderedList>? rangesFactory)
    {
        if (rangesFactory is null)
            return;

        foreach (var ipRange in rangesFactory())
            inserter.Insert(action, ipRange.FirstIpAddress.GetAddressBytes(), ipRange.LastIpAddress.GetAddressBytes());
    }
}
