using VpnHood.Core.Filtering.Abstractions;

namespace VpnHood.Core.Filtering.Sqlite;

// Generic concrete builder — the string twin of IpRangeListDbBuilder: stores plain domain lists into the
// db's per-action tables, exactly as given (no merge algebra — the db mirrors its sources). All inputs are
// factories, invoked lazily — the signature on every EnsureAsync (so keep it cheap: hashes/stat, never
// parse), the domain sets only on the rare rebuild path. An omitted or empty set leaves its table empty:
// no force-include lane for include, nothing to match for exclude/block.
/// <summary>
/// Provides the build of a split domain database from plain lists of domain names.
/// </summary>
/// <param name="sourceSignatureFactory">Gives the source's signature (<see cref="SplitDbBuilder.GetSourceSignature"/>).</param>
/// <param name="includesFactory">Gives the include set, or <see langword="null"/> to leave it empty.</param>
/// <param name="excludesFactory">Gives the exclude set, or <see langword="null"/> to leave it empty.</param>
/// <param name="blocksFactory">Gives the block set, or <see langword="null"/> to leave it empty.</param>
/// <remarks>
/// Each name is stored as <see cref="DomainUtils.NormalizeDomain"/> gives it, with its labels reversed; a blank one is
/// dropped. The signature's factory runs at every <see cref="SplitDbBuilder.EnsureAsync"/>, so keep it cheap; the
/// sets' factories run only when a build runs.
/// </remarks>
public class DomainListDbBuilder(
    Func<string> sourceSignatureFactory,
    Func<IReadOnlyList<string>>? includesFactory = null,
    Func<IReadOnlyList<string>>? excludesFactory = null,
    Func<IReadOnlyList<string>>? blocksFactory = null)
    : SplitDomainDbBuilder
{
    /// <inheritdoc/>
    /// <returns>What the signature's factory gives.</returns>
    public override string GetSourceSignature() => sourceSignatureFactory();

    /// <inheritdoc/>
    /// <remarks>It inserts each given set's names.</remarks>
    /// <exception cref="Microsoft.Data.Sqlite.SqliteException">SQLite refused a row.</exception>
    /// <exception cref="OperationCanceledException">The build's cancellation was requested.</exception>
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
