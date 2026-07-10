using Microsoft.Extensions.Logging;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Dtos;
using VpnHood.AppLib.Settings;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Filtering.Sqlite;
using VpnHood.Core.IpLocations;
using VpnHood.Core.IpLocations.Providers.Offlines;
using VpnHood.Core.IpLocations.Providers.Onlines;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Services;

public class LocationService : IRegionProvider
{
    private readonly AppSettingsService _settingsService;
    private readonly bool _useExternalLocationService;
    private readonly TimeSpan _locationServiceTimeout;
    private readonly CountryInfoService _countryInfoService = new();
    private readonly byte[]? _ipLocationZipData;
    private string? _ipLocationAssetHash;
    public event EventHandler? StateChanged;

    public bool IsFindingCountryCode { get; private set; }
    public bool IsFindingCountryIpRange { get; private set; }
    public IIpRangeLocationProvider? IpRangeLocationProvider { get; }

    public LocationService(AppSettingsService settingsService,
        bool useInternalLocationService,
        bool useExternalLocationService,
        TimeSpan locationServiceTimeout,
        byte[]? ipLocationZipData)
    {
        _settingsService = settingsService;
        _useExternalLocationService = useExternalLocationService;
        _locationServiceTimeout = locationServiceTimeout;
        _ipLocationZipData = ipLocationZipData;

        // IpRangeLocationProvider
        if (useInternalLocationService) {
            ArgumentNullException.ThrowIfNull(ipLocationZipData, "Internal location service needs IpLocationZipData.");
            IpRangeLocationProvider = new LocalIpRangeLocationProvider(
                () => new ZipArchive(new MemoryStream(ipLocationZipData)),
                () => GetClientCountryCode(false));
        }
    }

    public string GetClientCountryCode(bool allowVpnServer)
    {
        // try by server 
        if (allowVpnServer && _settingsService.Settings.ClientIpLocationByServer != null)
            return _settingsService.Settings.ClientIpLocationByServer.CountryCode;

        // try by client service providers
        return _settingsService.Settings.ClientIpLocation?.CountryCode
               ?? RegionInfo.CurrentRegion.Name;
    }

    public CountryInfo? TryGetClientCountryInfo()
    {
        return TryGetCountryInfo(RegionInfo.CurrentRegion.Name);
    }

    public CountryInfo? TryGetCountryInfo(string countryCode)
    {
        try {
            var cultureInfo = CultureInfo.DefaultThreadCurrentUICulture ?? CultureInfo.CurrentUICulture;
            if (!string.IsNullOrEmpty(_settingsService.UserSettings.CultureCode))
                cultureInfo = new CultureInfo(_settingsService.UserSettings.CultureCode);

            return _countryInfoService.GetCountryInfo(countryCode, cultureInfo);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(ex, "Could not get country info for country code: {CountryCode}", countryCode);
            return null;
        }
    }

    public Task<string> GetClientCountryCodeAsync(bool allowVpnServer, CancellationToken cancellationToken)
    {
        return GetClientCountryCodeAsync(allowVpnServer: allowVpnServer, allowCache: true, cancellationToken);
    }

    public async Task<string> GetClientCountryCodeAsync(bool allowVpnServer, bool allowCache,
        CancellationToken cancellationToken)
    {
        var ipLocation = await GetClientIpLocation(allowVpnServer: allowVpnServer, allowCache: allowCache, cancellationToken);
        return ipLocation?.CountryCode ?? RegionInfo.CurrentRegion.Name;
    }

    private readonly AsyncLock _currentLocationLock = new();

    private async Task<IpLocation?> GetClientIpLocation(bool allowVpnServer, bool allowCache,
        CancellationToken cancellationToken)
    {
        using var scopeLock = await _currentLocationLock.LockAsync(cancellationToken);

        if (allowCache) {
            if (allowVpnServer && _settingsService.Settings.ClientIpLocationByServer?.CountryCode != null)
                return _settingsService.Settings.ClientIpLocationByServer;

            // try by client service providers
            if (_settingsService.Settings.ClientIpLocation != null)
                return _settingsService.Settings.ClientIpLocation;
        }

        // try to get the current ip location by local service
        var ipLocation = await TryGetCurrentIpLocation(cancellationToken).Vhc();
        if (ipLocation != null)
            return ipLocation;

        // try to use cache if it could not get by local service
        if (allowVpnServer && _settingsService.Settings.ClientIpLocationByServer?.CountryCode != null)
            return _settingsService.Settings.ClientIpLocationByServer;

        // try by client service providers
        return _settingsService.Settings.ClientIpLocation;
    }

    private async Task<IpLocation?> TryGetCurrentIpLocation(CancellationToken cancellationToken)
    {
        // even IpRangeLocationProvider need external service to get current ip,
        // so if external service is disabled, return null
        if (!_useExternalLocationService)
            return null;

        try {
            VhLogger.Instance.LogDebug("Getting Country from external location service...");

            IsFindingCountryCode = true;
            FireConnectionStateChanged();

            using var httpClient = new HttpClient();
            const string userAgent = "VpnHood-Client";
            var providers = new List<ICurrentIpLocationProvider> {
                new CloudflareLocationProvider(httpClient, userAgent),
                new IpLocationIoProvider(httpClient, userAgent, apiKey: null)
            };

            // InternalLocationService needs current ip from external service, so it is inside the if block
            // It is slow on some devices, so it does not worth it
            // if (IpRangeLocationProvider is not null)
              //  providers.Add(IpRangeLocationProvider);

            var compositeProvider = new CompositeCurrentIpLocationProvider(
                VhLogger.Instance, providers, providerTimeout: _locationServiceTimeout);
            var ipLocation = await compositeProvider.GetCurrentLocation(cancellationToken).Vhc();
            _settingsService.Settings.ClientIpLocation = ipLocation;
            _settingsService.Settings.Save();
            return ipLocation;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not find country code.");
            return null;
        }
        finally {
            IsFindingCountryCode = false;
            FireConnectionStateChanged();
        }
    }

    public CountryInfo[] GetCountries()
    {
        // region my return 001 for word, but it is not a valid country code, so filter it out,
        // and also filter out any region with numeric name, just in case
        var countryInfos = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
            .Select(culture => new RegionInfo(culture.Name))
            .Where(region => !string.IsNullOrEmpty(region.Name) && !int.TryParse(region.Name, out _) )
            .DistinctBy(region => region.Name)
            .Select(region => TryGetCountryInfo(region.Name))
            .Where(countryInfo => countryInfo != null)
            .OrderBy(countryInfo => countryInfo!.TranslatedName)
            .ToArray();

        return countryInfos!;
    }

    public async Task<CountryInfo[]> GetSupportedSplitCountries(CancellationToken cancellationToken)
    {
        if (IpRangeLocationProvider is null)
            return [];

        // get all countries from IpRangeLocationProvider
        var splitByCountries = await IpRangeLocationProvider.GetCountryCodes(cancellationToken);
        var countryInfos = GetCountries()
            .Where(country => splitByCountries.Contains(country.CountryCode))
            .ToArray();

        return countryInfos;
    }

    // Build or reuse the on-disk split-country db for the current SplitCountryMode and return how the
    // client should interpret membership (SqliteIpFilter semantics). Returns Default when there is no
    // country split; on failure it falls back to IncludeAll (same policy as the old range materialization).
    // The (potentially huge) country ranges never enter memory — they stream from the zip into SQLite.
    public async Task<FilterAction> EnsureSplitIpDb(string dbPath, CancellationToken cancellationToken)
    {
        var splitCountryMode = _settingsService.Settings.UserSettings.SplitCountryMode;
        if (splitCountryMode is SplitCountryMode.IncludeAll)
            return FilterAction.Default;

        try {
            // set loading state
            IsFindingCountryIpRange = true;
            FireConnectionStateChanged();

            if (IpRangeLocationProvider is null || _ipLocationZipData is null)
                throw new InvalidOperationException("Could not use internal location service because it is disabled.");

            // resolve the selected countries
            string[] countryCodes = splitCountryMode is SplitCountryMode.ExcludeMyCountry
                ? [await GetSplitMyCountryCodeAsync(cancellationToken).Vhc()]
                : _settingsService.UserSettings.SplitCountries;

            // short path: store whichever of (selected, complement) is smaller and flip the action to match,
            // so an "everything except one" selection builds a one-country db
            var availableCodes = await IpRangeLocationProvider.GetCountryCodes(cancellationToken).Vhc();
            var (storedCodes, action) = ResolveSplitIpDbSelection(availableCodes, countryCodes,
                splitCountryMode is SplitCountryMode.IncludeList ? FilterAction.Include : FilterAction.Exclude);

            VhLogger.Instance.LogInformation(
                "Preparing split-country filter db... Mode: {Mode}, Action: {Action}, Countries: {Countries}",
                splitCountryMode, action, string.Join(',', storedCodes));

            var zipData = _ipLocationZipData;
            await SplitIpDbManager.EnsureAsync(dbPath,
                () => new ZipArchive(new MemoryStream(zipData)),
                storedCodes, GetIpLocationAssetHash(), cancellationToken).Vhc();

            return action;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not prepare the split-country filter db.");
            _settingsService.Settings.UserSettings.SplitCountryMode = SplitCountryMode.IncludeAll;
            _settingsService.Settings.Save();
            return FilterAction.Default;
        }
        finally {
            IsFindingCountryIpRange = false;
            FireConnectionStateChanged();
        }
    }

    // do not use cache and server country code, maybe client on satellite, and they need to split their own country IPs
    private async Task<string> GetSplitMyCountryCodeAsync(CancellationToken cancellationToken)
    {
        var countryCode = await GetClientCountryCodeAsync(allowVpnServer: false, allowCache: false, cancellationToken).Vhc();
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

    // Identifies the ip-location asset build so SplitIpDbManager can detect a changed asset. Prefer the
    // zip's own _checksum.txt (stamped at asset build time); fall back to hashing the zip bytes.
    private string GetIpLocationAssetHash()
    {
        if (_ipLocationAssetHash != null)
            return _ipLocationAssetHash;

        using var zip = new ZipArchive(new MemoryStream(_ipLocationZipData!));
        var entry = zip.GetEntry("_checksum.txt");
        if (entry != null) {
            using var reader = new StreamReader(entry.Open());
            _ipLocationAssetHash = reader.ReadToEnd().Trim();
        }
        else {
            _ipLocationAssetHash = Convert.ToHexString(MD5.HashData(_ipLocationZipData!));
        }

        return _ipLocationAssetHash;
    }


    private void FireConnectionStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        IpRangeLocationProvider?.Dispose();
    }
}