using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Filtering.Sqlite;

// Generic concrete builder: stores an already-merged IpRangeOrderedList as-is. Both inputs are factories,
// invoked lazily — the signature on every EnsureAsync (so keep it cheap: hashes/stat, never parse), the
// ranges only on the rare rebuild path.
public class IpRangeListDbBuilder(Func<IpRangeOrderedList> ipRangesFactory, Func<string> sourceSignatureFactory)
    : SplitIpDbBuilder
{
    protected override string BuildSourceSignature() => sourceSignatureFactory();

    protected override Task InsertRangesAsync(SplitIpDbInserter inserter, CancellationToken cancellationToken)
    {
        foreach (var ipRange in ipRangesFactory())
            inserter.Insert(ipRange.FirstIpAddress.GetAddressBytes(), ipRange.LastIpAddress.GetAddressBytes());
        return Task.CompletedTask;
    }
}
