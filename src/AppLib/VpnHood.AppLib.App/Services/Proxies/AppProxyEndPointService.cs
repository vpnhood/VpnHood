using VpnHood.AppLib.Abstractions.Device;
using VpnHood.AppLib.Api.Proxies;
using VpnHood.AppLib.App.DtoConverters;
using VpnHood.AppLib.Api.Settings;
using VpnHood.AppLib.App.Settings;
using VpnHood.Core.Client.VpnServices.Manager;
using VpnHood.Net.IpLocations;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Core.Proxies.Management.Abstractions.Options;
using VpnHood.Core.Proxies.Management.Sqlite;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Generics;
using VpnHood.Net.Toolkit.Utils;
using CoreProxy = VpnHood.Core.Proxies.Management.Abstractions;
// the store and the parser are the engine's; what this service hands out is the contract's
using ProxyEndPoint = VpnHood.AppLib.Api.Proxies.ProxyEndPoint;
using ProxyEndPointStatus = VpnHood.AppLib.Api.Proxies.ProxyEndPointStatus;

namespace VpnHood.AppLib.App.Services.Proxies;

public class AppProxyEndPointService(
    string dbPath,
    IIpLocationProvider? ipLocationProvider,
    IDeviceUiProvider deviceUiProvider,
    VpnServiceManager vpnServiceManager,
    AppSettingsService settingsService)
{
    // defer opening the shared endpoint db until proxy data is first used
    private readonly Lazy<IProxyEndPointStore> _lazyStore = new(() => new ProxyEndPointStore(dbPath));
    private IProxyEndPointStore Store => _lazyStore.Value;
    private readonly HostCountryResolver? _hostCountryResolver = ipLocationProvider != null ? new HostCountryResolver(ipLocationProvider) : null;
    private bool _pendingResetStates;
    private bool? _hasCustomEndPointsCache;
    private DateTime _hasCustomEndPointsCacheTime = DateTime.MinValue;
    private static readonly TimeSpan HasCustomEndPointsCacheTimeout = TimeSpan.FromSeconds(10);
    private AppProxySettings ProxySettings => settingsService.UserSettings.ProxySettings;

    public bool IsProxyEndPointActive {
        get {
            return ProxySettings.Mode switch {
                AppProxyMode.NoProxy => false,
                AppProxyMode.Device =>
                    deviceUiProvider.IsProxySettingsSupported &&
                    deviceUiProvider.GetProxySettings() != null,
                // this state property cannot await; serve the last-known value and refresh it
                // in the background. Mutations refresh the cache themselves, so it is only ever
                // stale when the core process changes the db (url auto-update)
                _ => HasCustomEndPointsCached()
            };
        }
    }

    private bool HasCustomEndPointsCached()
    {
        if (_hasCustomEndPointsCache is null ||
            FastDateTime.UtcNow - _hasCustomEndPointsCacheTime > HasCustomEndPointsCacheTimeout)
            _ = VhUtils.TryInvokeAsync("Refresh custom proxy endpoint existence",
                RefreshHasCustomEndPoints);

        return _hasCustomEndPointsCache ?? false;
    }

    private async Task<bool> RefreshHasCustomEndPoints()
    {
        _hasCustomEndPointsCache = await Store.Count().Vhc() > 0;
        _hasCustomEndPointsCacheTime = FastDateTime.UtcNow;
        return _hasCustomEndPointsCache.Value;
    }

    private CoreProxy.ProxyEndPoint? GetDeviceProxyEndPoint()
    {
        if (!deviceUiProvider.IsProxySettingsSupported)
            return null;

        var deviceProxySettings = deviceUiProvider.GetProxySettings();
        if (deviceProxySettings?.ProxyUrl is null)
            return null;

        return VhUtils.TryInvoke("Parse device proxy url",
            () => ProxyEndPointParser.FromUrl(deviceProxySettings.ProxyUrl));
    }

    public AppProxyEndPointInfo? GetDeviceProxy()
    {
        var deviceProxyEndPoint = GetDeviceProxyEndPoint();
        if (deviceProxyEndPoint is null)
            return null;

        // the device proxy is not stored; while connected in Device mode its status is the
        // session status reported by the core's single-proxy connector, projected onto the
        // endpoint dto (rating fields stay at their defaults; nothing rates a device proxy)
        var sessionStatus = ProxySettings.Mode is AppProxyMode.Device
            ? vpnServiceManager.ConnectionInfo.ProxyConnectorStatus?.SessionStatus
            : null;

        var status = new CoreProxy.ProxyEndPointStatus();
        if (sessionStatus != null) {
            status.SucceededCount = sessionStatus.SucceededCount;
            status.FailedCount = sessionStatus.FailedCount;
            status.Latency = sessionStatus.Latency;
            status.LastSucceeded = sessionStatus.LastSucceeded;
            status.LastFailed = sessionStatus.LastFailed;
            status.ErrorMessage = sessionStatus.ErrorMessage;
        }

        return new AppProxyEndPointInfo {
            EndPoint = deviceProxyEndPoint.ToAppDto(),
            CountryCode = null,
            Status = status.ToAppDto()
        };
    }

    public async Task<ListResult<AppProxyEndPointInfo>> ListProxies(
        string? search = null,
        bool includeSucceeded = true,
        bool includeFailed = true,
        bool includeUnknown = true,
        bool includeDisabled = true,
        int? recordIndex = null,
        int? recordCount = null)
    {
        // filtering, ordering and paging run inside the store; only the requested page is loaded
        var result = await Store.List(new ProxyEndPointStoreListParams {
            Search = search,
            IncludeSucceeded = includeSucceeded,
            IncludeFailed = includeFailed,
            IncludeUnknown = includeUnknown,
            IncludeDisabled = includeDisabled,
            RecordIndex = recordIndex ?? 0,
            RecordCount = recordCount ?? int.MaxValue
        }).Vhc();

        return new ListResult<AppProxyEndPointInfo> {
            Items = [.. result.Items.Select(ToAppInfo)],
            TotalCount = result.TotalCount
        };
    }

    public async Task<AppProxyEndPointInfo> Get(string id)
    {
        var record = await Store.Get(id).Vhc() ??
                     throw new KeyNotFoundException($"ProxyEndPoint not found. Id: {id}");
        return ToAppInfo(record);
    }

    public async Task<AppProxyEndPointInfo> Add(ProxyEndPoint proxyEndPoint)
    {
        var endPoint = ProxyEndPointParser.Normalize(proxyEndPoint.ToEngine());

        // adding an existing endpoint updates it and keeps its status
        await Store.Upsert([new ProxyEndPointRecord { EndPoint = endPoint }]).Vhc();
        await RefreshHasCustomEndPoints().Vhc();
        settingsService.Save(); // fire changes

        return await Get(endPoint.Id).Vhc();
    }

    public async Task<AppProxyEndPointInfo> Update(string id, ProxyEndPoint proxyEndPoint)
    {
        var endPoint = ProxyEndPointParser.Normalize(proxyEndPoint.ToEngine());
        var oldRecord = await Store.Get(id).Vhc() ??
                        throw new KeyNotFoundException($"ProxyEndPoint not found. Id: {id}");

        // keep the status even when the natural key (and so the id) changed
        if (endPoint.Id != id)
            await Store.Delete([id]).Vhc();

        await Store.Upsert([
            new ProxyEndPointRecord {
                EndPoint = endPoint,
                Status = oldRecord.Status,
                CountryCode = oldRecord.CountryCode
            }
        ], keepExistingStatus: false).Vhc();

        settingsService.Save(); // fire changes
        return await Get(endPoint.Id).Vhc();
    }

    public async Task Delete(string proxyEndPointId)
    {
        await Store.Delete([proxyEndPointId]).Vhc();
        await RefreshHasCustomEndPoints().Vhc();
        settingsService.Save(); // fire changes
    }

    public async Task DeleteAll(DeleteAllOptions? options = null)
    {
        await Store.DeleteAll(options ?? new DeleteAllOptions()).Vhc();
        await RefreshHasCustomEndPoints().Vhc();
        settingsService.Save(); // fire changes
    }

    public async Task DisableAllFailed()
    {
        await Store.DisableAllFailed().Vhc();
        settingsService.Save(); // fire changes
    }

    public async Task Import(string content)
    {
        // parse new endpoints
        var newProxyEndPoints = ProxyEndPointParser
            .ExtractFromContent(content)
            .Select(ProxyEndPointParser.FromUrl)
            .ToArray();

        // merge into the shared store with the standard priority rules
        var autoUpdateOptions = ProxySettings.AutoUpdateOptions;
        await Store.Merge(newProxyEndPoints, autoUpdateOptions.MaxItemCount, autoUpdateOptions.MaxPenalty,
            autoUpdateOptions.RemoveDuplicateIps).Vhc();

        await RefreshHasCustomEndPoints().Vhc();
        settingsService.Save(); // fire changes
    }

    public async Task ReloadUrl(CancellationToken cancellationToken)
    {
        // fetch endpoints from url and merge
        // ReSharper disable once ShortLivedHttpClient
        using var httpClient = new HttpClient();
        var content = await httpClient.GetStringAsync(ProxySettings.AutoUpdateOptions.Url, cancellationToken);
        await Import(content).Vhc();
    }

    public async Task ResetStates()
    {
        await Store.ResetStatuses().Vhc();

        // make the next GetProxyOptions tell a live core to discard its in-memory statuses
        // instead of flushing them back over the reset
        _pendingResetStates = true;
        settingsService.Save(); // fire changes
    }

    // ReSharper disable once UnusedMember.Local
    private async Task UpdateCountryCode(IEnumerable<AppProxyEndPointInfo> proxyEndPointInfos,
        CancellationToken cancellationToken)
    {
        if (_hostCountryResolver is null)
            return;

        // resolve all host with domain name
        // ReSharper disable once UseCollectionExpression
        proxyEndPointInfos = proxyEndPointInfos.Where(x => x.CountryCode is null).ToArray();
        var hostCountries = await _hostCountryResolver
            .GetHostCountries(proxyEndPointInfos.Select(x => x.EndPoint.Host), cancellationToken);

        foreach (var proxyEndPointInfo in proxyEndPointInfos) {
            var countryCode = hostCountries.GetValueOrDefault(proxyEndPointInfo.EndPoint.Host);
            if (countryCode != null)
                await Store.SetCountryCode(proxyEndPointInfo.EndPoint.Id, countryCode).Vhc();
        }
    }

    public async Task<ProxyOptions> GetProxyOptions()
    {
        var resetStates = _pendingResetStates;
        _pendingResetStates = false;

        // this decides Managed vs None for a real connection, so read fresh from the store.
        // an auto-update url counts even with an empty store; the core downloads the initial list
        var hasCustomEndPoints = ProxySettings.Mode is AppProxyMode.Manual &&
                                 (await RefreshHasCustomEndPoints().Vhc() ||
                                  ProxySettings.AutoUpdateOptions.Url != null);

        var mode = ProxySettings.Mode switch {
            AppProxyMode.Device => ProxyMode.Simple,
            AppProxyMode.Manual when hasCustomEndPoints => ProxyMode.Managed,
            _ => ProxyMode.None
        };

        // the device proxy travels inline; it never enters the shared store
        var singleProxyEndPoint = mode is ProxyMode.Simple ? GetDeviceProxyEndPoint() : null;
        if (mode is ProxyMode.Simple && singleProxyEndPoint is null)
            mode = ProxyMode.None;

        return new ProxyOptions {
            Mode = mode,
            ProxyEndPoint = singleProxyEndPoint,
            ResetStates = resetStates,
            AutoUpdateOptions = ProxySettings.AutoUpdateOptions.ToEngine()
        };
    }

    private static AppProxyEndPointInfo ToAppInfo(ProxyEndPointRecord record)
    {
        return new AppProxyEndPointInfo {
            EndPoint = record.EndPoint.ToAppDto(),
            Status = record.Status.ToAppDto(),
            CountryCode = record.CountryCode
        };
    }
}
