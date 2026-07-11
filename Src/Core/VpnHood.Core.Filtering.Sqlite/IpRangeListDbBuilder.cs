using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Filtering.Sqlite;

// Generic concrete builder: stores plain IpRangeOrderedLists into the db's per-action tables, exactly as
// given (no merge algebra — the db mirrors its sources). All inputs are factories, invoked lazily — the
// signature on every EnsureAsync (so keep it cheap: hashes/stat, never parse), the range sets only on the
// rare rebuild path. An omitted or empty set leaves its tables empty: no constraint for include, nothing
// to match for exclude/block.
public class IpRangeListDbBuilder(
    Func<string> sourceSignatureFactory,
    Func<IpRangeOrderedList>? includesFactory = null,
    Func<IpRangeOrderedList>? excludesFactory = null,
    Func<IpRangeOrderedList>? blocksFactory = null)
    : SplitIpDbBuilder
{
    protected override string BuildSourceSignature() => sourceSignatureFactory();

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
