using Microsoft.Extensions.Logging;
using System.IO.Compression;
using System.Security.Cryptography;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Dtos;
using VpnHood.AppLib.Settings;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Filtering.Sqlite;
using VpnHood.Core.IpLocations;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Services;

// Prepares the on-disk split-country filter db before connecting. Uses the app's ip-range provider
// as a data source; the client country itself comes from AppRegionInfo and country names from
// AppCountryInfo; nothing is discovered here.
public class SplitCountryService(
    AppSettingsService settingsService,
    IPremiumFeatureChecker premiumFeatureChecker,
    IIpRangeLocationProvider? ipRangeLocationProvider,
    Lazy<byte[]>? ipLocationZipData)
{
    private string? _ipLocationAssetHash;
    public event EventHandler? StateChanged;

    public bool IsBusy { get; private set; }

    public async Task<CountryInfo[]> GetSupportedSplitCountries(CancellationToken cancellationToken)
    {
        if (ipRangeLocationProvider is null)
            return [];

        // get all countries from the ip-range provider
        var splitByCountries = await ipRangeLocationProvider.GetCountryCodes(cancellationToken).Vhc();
        var countryInfos = AppCountryInfo.GetAll()
            .Where(country => splitByCountries.Contains(country.CountryCode))
            .ToArray();

        return countryInfos;
    }

    // Build or reuse the on-disk split-country db and return its path, or null when the feature is
    // inactive — this service owns its whole activity decision (SplitCountryMode + premium plan). The
    // selected countries' ranges are written into the db's include or exclude set — the db
    // self-describes what membership means, so only its path travels. The file name carries the source
    // signature, so a changed selection builds a NEW file and a running VpnService can keep the
    // superseded db open until it live-swaps to the returned path. Failures propagate and fail to
    // connect: a split the user configured is enforced or the connection does not proceed — never
    // silently skipped.
    // The (potentially huge) country ranges never enter memory — they stream from the zip into SQLite.
    public async Task<string?> EnsureSplitIpDb(string dbFolder, CancellationToken cancellationToken)
    {
        var splitCountryMode = settingsService.Settings.UserSettings.SplitCountryMode;
        if (splitCountryMode is SplitCountryMode.IncludeAll ||
            !premiumFeatureChecker.CheckPremiumFeature(AppFeature.SplitCountry))
            return null;

        try {
            // set loading state
            IsBusy = true;
            StateChanged?.Invoke(this, EventArgs.Empty);

            if (ipRangeLocationProvider is null || ipLocationZipData is null)
                throw new InvalidOperationException("Could not split by country because the ip-location asset is not provided.");

            // resolve the selected countries
            string[] countryCodes = splitCountryMode is SplitCountryMode.ExcludeMyCountry
                ? [GetSplitMyCountryCode()]
                : settingsService.UserSettings.SplitCountries;

            // short path: store whichever of (selected, complement) is smaller in the matching set,
            // so an "everything except one" selection builds a one-country db
            var availableCodes = await ipRangeLocationProvider.GetCountryCodes(cancellationToken).Vhc();
            var (storedCodes, action) = ResolveSplitIpDbSelection(availableCodes, countryCodes,
                splitCountryMode is SplitCountryMode.IncludeList ? FilterAction.Include : FilterAction.Exclude);

            // an empty include set stores no constraint (tunnel everything) — the opposite of what an
            // include list with no known country means; fail loud instead of silently ignoring the split
            if (storedCodes.Length == 0 && action is FilterAction.Include)
                throw new InvalidOperationException("The split country include list contains no known country.");

            VhLogger.Instance.LogInformation(
                "Preparing split-country filter db... Mode: {Mode}, Action: {Action}, Countries: {Countries}",
                splitCountryMode, action, string.Join(',', storedCodes));

            var dbBuilder = new SplitCountryDbBuilder(
                () => new ZipArchive(new MemoryStream(ipLocationZipData.Value)),
                storedCodes, GetIpLocationAssetHash(), action);

            var dbPath = Path.Combine(dbFolder,
                $"split-country.{VhUtils.GetHexStringSha256(dbBuilder.GetSourceSignature(), 16)}.db");
            await dbBuilder.EnsureAsync(dbPath, cancellationToken).Vhc();

            return dbPath;
        }
        finally {
            IsBusy = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // country comes from the current region; never discovered
    private static string GetSplitMyCountryCode()
    {
        var countryCode = AppRegionInfo.CurrentRegion.Name;
        VhLogger.Instance.LogInformation("Client CountryCode is: {CountryCode}",
            AppCountryInfo.TryGet(countryCode)?.EnglishName);
        return countryCode;
    }

    // A selection and its complement express the same split when the target set flips (Include<->Exclude),
    // so deterministically store the strictly smaller one — an "all countries except one" selection stores
    // one country, not 244. Never flip INTO an empty include set though: an empty include table means "no
    // constraint" (tunnel everything), the opposite of "exclude every known country".
    // Selected codes unknown to the asset contribute no ranges and are dropped before comparing.
    internal static (string[] StoredCodes, FilterAction Action) ResolveSplitIpDbSelection(
        string[] availableCodes, string[] selectedCodes, FilterAction action)
    {
        var available = availableCodes
            .Select(x => x.ToUpperInvariant())
            .Distinct()
            .ToArray();

        var selected = selectedCodes
            .Select(x => x.ToUpperInvariant())
            .Distinct()
            .Where(available.Contains)
            .ToArray();

        var complement = available
            .Except(selected)
            .ToArray();

        if (complement.Length >= selected.Length)
            return (selected, action);

        if (complement.Length == 0 && action is FilterAction.Exclude)
            return (selected, action); // don't flip: ([], Include) would constrain nothing

        return (complement, action is FilterAction.Include ? FilterAction.Exclude : FilterAction.Include);
    }

    // Identifies the ip-location asset build so SplitCountryDbBuilder can detect a changed asset. Prefer the
    // zip's own _checksum.txt (stamped at asset build time); fall back to hashing the zip bytes.
    private string GetIpLocationAssetHash()
    {
        if (_ipLocationAssetHash != null)
            return _ipLocationAssetHash;

        var ipLocationData = ipLocationZipData?.Value
            ?? throw new InvalidOperationException("The ip-location asset is not provided.");
        using var zip = new ZipArchive(new MemoryStream(ipLocationData));
        var entry = zip.GetEntry("_checksum.txt");
        if (entry != null) {
            using var reader = new StreamReader(entry.Open());
            _ipLocationAssetHash = reader.ReadToEnd().Trim();
        }
        else {
            _ipLocationAssetHash = Convert.ToHexString(MD5.HashData(ipLocationData));
        }

        return _ipLocationAssetHash;
    }

    // Split-country source for the split-ip db: streams the selected countries' serialized ranges from
    // the ip-location zip ({code}.ips entries, already sorted+unified) into the db's include or exclude
    // set — the db self-describes what membership means, nothing travels but its path. The zip layout
    // and the asset-hash signature are split-country business, so the builder lives inside the service
    // that owns them (internal: only this service constructs it; tests see it as a friend) — the
    // Filtering.Sqlite infrastructure stays context-agnostic.
    internal class SplitCountryDbBuilder(
        Func<ZipArchive> zipArchiveFactory,
        IReadOnlyCollection<string> countryCodes,
        string assetHash,
        FilterAction action)
        : SplitIpDbBuilder
    {
        // Asset build id + target set + distinct, upper-cased, ordinal-sorted codes. The action IS part
        // of the signature: the same codes stored as include vs exclude are different db contents.
        public override string GetSourceSignature() =>
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
}
