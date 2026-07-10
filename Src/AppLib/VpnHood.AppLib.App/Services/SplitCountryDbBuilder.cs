using System.IO.Compression;
using VpnHood.Core.Filtering.Sqlite;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.AppLib.Services;

// Split-country source for the split-ip db: streams the selected countries' serialized ranges from the
// ip-location zip ({code}.ips entries, already sorted+unified) without materializing IpRange objects.
// The zip layout and the asset-hash signature are split-country business, so they live here next to
// SplitCountryService — the Filtering.Sqlite infrastructure stays context-agnostic.
public class SplitCountryDbBuilder(
    Func<ZipArchive> zipArchiveFactory,
    IReadOnlyCollection<string> countryCodes,
    string assetHash)
    : SplitIpDbBuilder
{
    // Asset build id + distinct, upper-cased, ordinal-sorted codes. Mode is intentionally NOT part of the
    // signature (db content is mode-independent — see the inversion rule in docs/split-ip/split-country.md).
    protected override string BuildSourceSignature() =>
        assetHash + "|" + string.Join(',', countryCodes
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
                inserter.Insert(start, end);
        }
    }
}
