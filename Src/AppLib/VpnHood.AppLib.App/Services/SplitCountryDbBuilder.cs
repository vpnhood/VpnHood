using System.IO.Compression;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Filtering.Sqlite;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.AppLib.Services;

// Split-country source for the split-ip db: streams the selected countries' serialized ranges from the
// ip-location zip ({code}.ips entries, already sorted+unified) into the db's include or exclude set —
// the db self-describes what membership means, nothing travels but its path. The zip layout and the
// asset-hash signature are split-country business, so they live here next to SplitCountryService — the
// Filtering.Sqlite infrastructure stays context-agnostic.
public class SplitCountryDbBuilder(
    Func<ZipArchive> zipArchiveFactory,
    IReadOnlyCollection<string> countryCodes,
    string assetHash,
    FilterAction action)
    : SplitIpDbBuilder
{
    // Asset build id + target set + distinct, upper-cased, ordinal-sorted codes. The action IS part of the
    // signature: the same codes stored as include vs exclude are different db contents.
    protected override string BuildSourceSignature() =>
        assetHash + "|" + action + "|" + string.Join(',', countryCodes
            .Select(c => c.ToUpperInvariant())
            .Distinct()
            .OrderBy(c => c, StringComparer.Ordinal));

    protected override async Task InsertRangesAsync(SplitIpDbInserter inserter, CancellationToken cancellationToken)
    {
        // the factory (not an open archive) keeps the zip unopened on the common ensure-up-to-date path
        await using var zip = zipArchiveFactory();
        foreach (var countryCode in countryCodes) {
            var entry = zip.GetEntry($"{countryCode.ToLowerInvariant()}.ips");
            if (entry is null)
                continue; // unknown country code → nothing to add

            await using var stream = await entry.OpenAsync(cancellationToken);
            foreach (var (start, end) in IpRangeOrderedList.DeserializeRaw(stream))
                inserter.Insert(action, start, end);
        }
    }
}
