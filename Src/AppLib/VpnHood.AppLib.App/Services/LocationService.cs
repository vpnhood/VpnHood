using Microsoft.Extensions.Logging;
using System.Globalization;
using System.IO.Compression;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Dtos;
using VpnHood.AppLib.Settings;
using VpnHood.Core.IpLocations;
using VpnHood.Core.IpLocations.Providers.Offlines;
using VpnHood.Core.IpLocations.Providers.Onlines;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Services;

public class LocationService : IRegionProvider
{
    private readonly AppSettingsService _settingsService;
    private readonly bool _useExternalLocationService;
    private readonly TimeSpan _locationServiceTimeout;
    public event EventHandler? StateChanged;

    public bool IsFindingCountryCode { get; private set; }
    public bool IsFindingCountryIpRange { get; private set; }
    public IIpRangeLocationProvider? IpRangeLocationProvider { get; }

    public LocationService(AppSettingsService settingsService,
        bool useInternalLocationService,
        bool useExternalLocationService,
        TimeSpan locationServiceTimeout, byte[]? ipLocationZipData)
    {
        _settingsService = settingsService;
        _useExternalLocationService = useExternalLocationService;
        _locationServiceTimeout = locationServiceTimeout;

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

    public Task<string> GetClientCountryCodeAsync(bool allowVpnServer, CancellationToken cancellationToken)
    {
        return GetClientCountryCodeAsync(allowVpnServer: allowVpnServer, allowCache: true, cancellationToken);
    }

    public async Task<string> GetClientCountryCodeAsync(bool allowVpnServer, bool allowCache,
        CancellationToken cancellationToken)
    {
        var ipLocation =
            await GetClientIpLocation(allowVpnServer: allowVpnServer, allowCache: allowCache, cancellationToken);
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
        var ipLocation = await TryGetCurrentIpLocationByLocal(cancellationToken).Vhc();
        if (ipLocation != null)
            return ipLocation;

        // try to use cache if it could not get by local service
        if (allowVpnServer && _settingsService.Settings.ClientIpLocationByServer?.CountryCode != null)
            return _settingsService.Settings.ClientIpLocationByServer;

        // try by client service providers
        return _settingsService.Settings.ClientIpLocation;
    }

    private async Task<IpLocation?> TryGetCurrentIpLocationByLocal(CancellationToken cancellationToken)
    {
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
            if (IpRangeLocationProvider is not null)
                providers.Add(IpRangeLocationProvider);

            var compositeProvider = new CompositeCurrentIpLocationProvider(VhLogger.Instance, providers,
                providerTimeout: _locationServiceTimeout);
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

    public static CountryInfo[] GetCountries()
    {
        var countryInfos = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
            .Select(culture => new RegionInfo(culture.Name))
            .Where(region => !string.IsNullOrEmpty(region.Name))
            .DistinctBy(region => region.Name)
            .OrderBy(region => region.EnglishName)
            .Select(region => new CountryInfo {
                CountryCode = region.Name,
                EnglishName = region.EnglishName
            })
            .ToArray();
        return countryInfos;
    }

    public async Task<CountryInfo[]> GetSupportedSplitByCountries(CancellationToken cancellationToken)
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

    public async Task<IpRangeOrderedList> GetIncludeCountryIpRanges(CancellationToken cancellationToken)
    {
        var ipRanges = IpNetwork.All.ToIpRanges();
        var splitByCountryMode = _settingsService.Settings.UserSettings.SplitByCountryMode;
        if (splitByCountryMode is SplitByCountryMode.IncludeAll)
            return ipRanges;

        try {
            // set loading state
            IsFindingCountryIpRange = true;
            FireConnectionStateChanged();

            if (IpRangeLocationProvider is null)
                throw new InvalidOperationException("Could not use internal location service because it is disabled.");

            // calculate include country IPs
            if (splitByCountryMode is SplitByCountryMode.IncludeList) {
                VhLogger.Instance.LogInformation("Calculating include country IP ranges...");
                var countryIpRanges = new List<IpRange>();
                foreach (var country in _settingsService.UserSettings.SplitByCountries)
                    countryIpRanges.AddRange(
                        await IpRangeLocationProvider.GetIpRanges(country, cancellationToken).Vhc());
                ipRanges = countryIpRanges.ToOrderedList();
            }

            // calculate exclude country IPs
            if (splitByCountryMode is SplitByCountryMode.ExcludeList) {
                VhLogger.Instance.LogInformation("Calculating exclude country IP ranges...");
                var countryIpRanges = new List<IpRange>();
                foreach (var country in _settingsService.UserSettings.SplitByCountries)
                    countryIpRanges.AddRange(
                        await IpRangeLocationProvider.GetIpRanges(country, cancellationToken).Vhc());
                ipRanges = ipRanges.Exclude(countryIpRanges);
            }


            if (splitByCountryMode is SplitByCountryMode.ExcludeMyCountry) {
                VhLogger.Instance.LogInformation("Calculating exclude my country IP ranges...");

                // do not use cache and server country code, maybe client on satellite, and they need to split their own country IPs 
                var countryCode =
                    await GetClientCountryCodeAsync(allowVpnServer: false, allowCache: false, cancellationToken).Vhc();
                var countryIpRanges = await IpRangeLocationProvider.GetIpRanges(countryCode, cancellationToken).Vhc();
                VhLogger.Instance.LogInformation("Client CountryCode is: {CountryCode}",
                    VhUtils.TryGetCountryName(countryCode));
                ipRanges = ipRanges.Exclude(countryIpRanges);
            }

            return ipRanges;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not retrieve the requested countries ip ranges.");
            _settingsService.Settings.UserSettings.SplitByCountryMode = SplitByCountryMode.IncludeAll;
            _settingsService.Settings.Save();
            return IpNetwork.All.ToIpRanges();
        }
        finally {
            IsFindingCountryIpRange = false;
            FireConnectionStateChanged();
        }
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