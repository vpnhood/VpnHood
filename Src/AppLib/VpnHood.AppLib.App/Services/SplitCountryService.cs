using Microsoft.Extensions.Logging;
using System.IO.Compression;
using System.Security.Cryptography;
using VpnHood.AppLib.Settings;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Filtering.Sqlite;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Services;

// Prepares the on-disk split-country filter db before connecting. Uses LocationService only as a data
// source (client country, available country codes); the split policy, asset hashing and db orchestration
// live here so LocationService stays a pure region provider.
public class SplitCountryService(
    AppSettingsService settingsService,
    LocationService locationService,
    byte[]? ipLocationZipData)
{
    private string? _ipLocationAssetHash;
    public event EventHandler? StateChanged;

    public bool IsBusy { get; private set; }

    // Build or reuse the on-disk split-country db for the current SplitCountryMode and return how the
    // client should interpret membership (SqliteIpFilter semantics). Returns Default when there is no
    // country split. Failures propagate and fail the connect: a split the user configured is enforced or
    // the connection does not proceed — never silently skipped.
    // The (potentially huge) country ranges never enter memory — they stream from the zip into SQLite.
    public async Task<FilterAction> EnsureSplitIpDb(string dbPath, CancellationToken cancellationToken)
    {
        var splitCountryMode = settingsService.Settings.UserSettings.SplitCountryMode;
        if (splitCountryMode is SplitCountryMode.IncludeAll)
            return FilterAction.Default;

        try {
            // set loading state
            IsBusy = true;
            StateChanged?.Invoke(this, EventArgs.Empty);

            if (locationService.IpRangeLocationProvider is null || ipLocationZipData is null)
                throw new InvalidOperationException("Could not use internal location service because it is disabled.");

            // resolve the selected countries
            string[] countryCodes = splitCountryMode is SplitCountryMode.ExcludeMyCountry
                ? [await GetSplitMyCountryCodeAsync(cancellationToken).Vhc()]
                : settingsService.UserSettings.SplitCountries;

            // short path: store whichever of (selected, complement) is smaller and flip the action to match,
            // so an "everything except one" selection builds a one-country db
            var availableCodes = await locationService.IpRangeLocationProvider.GetCountryCodes(cancellationToken).Vhc();
            var (storedCodes, action) = ResolveSplitIpDbSelection(availableCodes, countryCodes,
                splitCountryMode is SplitCountryMode.IncludeList ? FilterAction.Include : FilterAction.Exclude);

            VhLogger.Instance.LogInformation(
                "Preparing split-country filter db... Mode: {Mode}, Action: {Action}, Countries: {Countries}",
                splitCountryMode, action, string.Join(',', storedCodes));

            var dbBuilder = new SplitCountryDbBuilder(
                () => new ZipArchive(new MemoryStream(ipLocationZipData)),
                storedCodes, GetIpLocationAssetHash());
            await dbBuilder.EnsureAsync(dbPath, cancellationToken).Vhc();

            return action;
        }
        finally {
            IsBusy = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // do not use cache and server country code, maybe client on satellite, and they need to split their own country IPs
    private async Task<string> GetSplitMyCountryCodeAsync(CancellationToken cancellationToken)
    {
        var countryCode = await locationService
            .GetClientCountryCodeAsync(allowVpnServer: false, allowCache: false, cancellationToken).Vhc();
        VhLogger.Instance.LogInformation("Client CountryCode is: {CountryCode}",
            VhUtils.TryGetCountryName(countryCode));
        return countryCode;
    }

    // The db is mode-independent (ranges stored as-is; the action travels in the descriptor), so a selection
    // and its complement express the same split. Deterministic rule: always store the strictly smaller set and
    // flip Include<->Exclude to match — an "all countries except one" selection stores one country, not 244.
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

        return (complement, action is FilterAction.Include ? FilterAction.Exclude : FilterAction.Include);
    }

    // Identifies the ip-location asset build so SplitCountryDbBuilder can detect a changed asset. Prefer the
    // zip's own _checksum.txt (stamped at asset build time); fall back to hashing the zip bytes.
    private string GetIpLocationAssetHash()
    {
        if (_ipLocationAssetHash != null)
            return _ipLocationAssetHash;

        using var zip = new ZipArchive(new MemoryStream(ipLocationZipData!));
        var entry = zip.GetEntry("_checksum.txt");
        if (entry != null) {
            using var reader = new StreamReader(entry.Open());
            _ipLocationAssetHash = reader.ReadToEnd().Trim();
        }
        else {
            _ipLocationAssetHash = Convert.ToHexString(MD5.HashData(ipLocationZipData!));
        }

        return _ipLocationAssetHash;
    }
}
