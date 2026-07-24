using VpnHood.Core.Filtering.Abstractions;

namespace VpnHood.Core.Filtering.Sqlite;

// Generic concrete builder — the string twin of IpRangeListDbBuilder: stores plain domain lists into the
// db's per-action tables, exactly as given (no merge algebra — the db mirrors its sources). All inputs are
// factories, invoked lazily — the signature on every EnsureAsync (so keep it cheap: hashes/stat, never
// parse), the domain sets only on the rare rebuild path. An omitted or empty set leaves its table empty:
// no force-include lane for include, nothing to match for exclude/block.
public class DomainListDbBuilder(
    Func<string> sourceSignatureFactory,
    Func<IReadOnlyList<string>>? includesFactory = null,
    Func<IReadOnlyList<string>>? excludesFactory = null,
    Func<IReadOnlyList<string>>? blocksFactory = null)
    : SplitDomainDbBuilder
{
    public override string GetSourceSignature() => sourceSignatureFactory();

    protected override Task InsertDomainsAsync(SplitDomainDbInserter inserter, CancellationToken cancellationToken)
    {
        InsertSet(inserter, FilterAction.Include, includesFactory);
        InsertSet(inserter, FilterAction.Exclude, excludesFactory);
        InsertSet(inserter, FilterAction.Block, blocksFactory);
        return Task.CompletedTask;
    }

    private static void InsertSet(SplitDomainDbInserter inserter, FilterAction action,
        Func<IReadOnlyList<string>>? domainsFactory)
    {
        if (domainsFactory is null)
            return;

        foreach (var domain in domainsFactory()) {
            // canonical form at insert time so lookups are a plain ordinal comparison (see SplitDomainDb);
            // blank entries can never match and are dropped, same as StaticDomainFilter
            var normalizedDomain = DomainUtils.NormalizeDomain(domain);
            if (normalizedDomain.Length > 0)
                inserter.Insert(action, DomainUtils.InvertDomain(normalizedDomain));
        }
    }
}
