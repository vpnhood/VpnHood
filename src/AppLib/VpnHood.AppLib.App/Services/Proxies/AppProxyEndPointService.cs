using Microsoft.Extensions.Logging;
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
using VpnHood.Net.Toolkit.Logging;
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
    AppSettingsService settingsService,
    ProxyEndPointCopy? endPointCopy)
{
    // the shared endpoint db, opened once, when proxy data is first used
    private readonly Lock _storeLock = new();
    private Task<IProxyEndPointStore>? _storeTask;
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
            _ = VhInvoker.TryInvokeAsync("refresh whether custom proxy endpoints exist", LogLevel.Warning,
                () => RefreshHasCustomEndPoints(CancellationToken.None));

        return _hasCustomEndPointsCache ?? false;
    }

    private async Task<bool> RefreshHasCustomEndPoints(CancellationToken cancellationToken)
    {
        var store = await GetStore(cancellationToken).Vhc();
        _hasCustomEndPointsCache = await store.Count(cancellationToken).Vhc() > 0;
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

        return VhInvoker.TryInvoke("parse the device's proxy URL", LogLevel.Debug,
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
        string? search,
        bool includeSucceeded,
        bool includeFailed,
        bool includeUnknown,
        bool includeDisabled,
        int? recordIndex,
        int? recordCount,
        CancellationToken cancellationToken)
    {
        // filtering, ordering and paging run inside the store; only the requested page is loaded
        var store = await GetStore(cancellationToken).Vhc();
        var result = await store.List(new ProxyEndPointStoreListParams {
            Search = search,
            IncludeSucceeded = includeSucceeded,
            IncludeFailed = includeFailed,
            IncludeUnknown = includeUnknown,
            IncludeDisabled = includeDisabled,
            RecordIndex = recordIndex ?? 0,
            RecordCount = recordCount ?? int.MaxValue
        }, cancellationToken).Vhc();

        return new ListResult<AppProxyEndPointInfo> {
            Items = result.Items.Select(ToAppInfo).ToArray(),
            TotalCount = result.TotalCount
        };
    }

    public async Task<AppProxyEndPointInfo> Get(string id, CancellationToken cancellationToken)
    {
        var store = await GetStore(cancellationToken).Vhc();
        var record = await store.Get(id, cancellationToken).Vhc() ??
                     throw new KeyNotFoundException($"ProxyEndPoint not found. Id: {id}");
        return ToAppInfo(record);
    }

    public async Task<AppProxyEndPointInfo> Add(ProxyEndPoint proxyEndPoint, CancellationToken cancellationToken)
    {
        var endPoint = ProxyEndPointParser.Normalize(proxyEndPoint.ToEngine());

        // adding an existing endpoint updates it and keeps its status
        var store = await GetStore(cancellationToken).Vhc();
        await store.Upsert([new ProxyEndPointRecord { EndPoint = endPoint }], keepExistingStatus: true,
            keepExistingEnabled: false, cancellationToken).Vhc();
        await UpdateCopy(store, [endPoint.Id], cancellationToken).Vhc();
        await RefreshHasCustomEndPoints(cancellationToken).Vhc();
        settingsService.Save(); // fire changes

        return await Get(endPoint.Id, cancellationToken).Vhc();
    }

    public async Task<AppProxyEndPointInfo> Update(string id, ProxyEndPoint proxyEndPoint, CancellationToken cancellationToken)
    {
        var endPoint = ProxyEndPointParser.Normalize(proxyEndPoint.ToEngine());
        var store = await GetStore(cancellationToken).Vhc();
        var oldRecord = await store.Get(id, cancellationToken).Vhc() ??
                        throw new KeyNotFoundException($"ProxyEndPoint not found. Id: {id}");

        // keep the status even when the natural key (and so the id) changed
        if (endPoint.Id != id)
            await store.Delete([id], cancellationToken).Vhc();

        await store.Upsert([
            new ProxyEndPointRecord {
                EndPoint = endPoint,
                Status = oldRecord.Status,
                CountryCode = oldRecord.CountryCode
            }
        ], keepExistingStatus: false, keepExistingEnabled: false, cancellationToken).Vhc();
        await UpdateCopy(store, [endPoint.Id], cancellationToken).Vhc();

        settingsService.Save(); // fire changes
        return await Get(endPoint.Id, cancellationToken).Vhc();
    }

    public async Task Delete(string proxyEndPointId, CancellationToken cancellationToken)
    {
        var store = await GetStore(cancellationToken).Vhc();
        await store.Delete([proxyEndPointId], cancellationToken).Vhc();
        await UpdateCopy(store, [], cancellationToken).Vhc();
        await RefreshHasCustomEndPoints(cancellationToken).Vhc();
        settingsService.Save(); // fire changes
    }

    public async Task DeleteAll(DeleteAllOptions? options, CancellationToken cancellationToken)
    {
        var store = await GetStore(cancellationToken).Vhc();
        await store.DeleteAll(options ?? new DeleteAllOptions(), cancellationToken).Vhc();
        await UpdateCopy(store, [], cancellationToken).Vhc();
        await RefreshHasCustomEndPoints(cancellationToken).Vhc();
        settingsService.Save(); // fire changes
    }

    public async Task DisableAllFailed(CancellationToken cancellationToken)
    {
        var store = await GetStore(cancellationToken).Vhc();
        await store.DisableAllFailed(cancellationToken).Vhc();
        await UpdateCopy(store, [], cancellationToken).Vhc();
        settingsService.Save(); // fire changes
    }

    // a list the person pasted: its proxies are the person's own
    public async Task Import(string content, CancellationToken cancellationToken)
    {
        var store = await GetStore(cancellationToken).Vhc();
        var newProxyEndPoints = await Merge(store, content, cancellationToken).Vhc();
        await UpdateCopy(store, newProxyEndPoints.Select(x => x.Id).ToArray(), cancellationToken).Vhc();
        await RefreshHasCustomEndPoints(cancellationToken).Vhc();
        settingsService.Save(); // fire changes
    }

    // the URL's list, which the URL gives again, so no copy keeps it
    public async Task ReloadUrl(CancellationToken cancellationToken)
    {
        // ReSharper disable once ShortLivedHttpClient
        using var httpClient = new HttpClient();
        var content = await httpClient.GetStringAsync(ProxySettings.AutoUpdateOptions.Url, cancellationToken).Vhc();
        var store = await GetStore(cancellationToken).Vhc();
        await Merge(store, content, cancellationToken).Vhc();
        await RefreshHasCustomEndPoints(cancellationToken).Vhc();
        settingsService.Save(); // fire changes
    }

    // the endpoints in the content, merged into the shared store with the standard priority rules
    private async Task<CoreProxy.ProxyEndPoint[]> Merge(IProxyEndPointStore store, string content,
        CancellationToken cancellationToken)
    {
        var newProxyEndPoints = ProxyEndPointParser
            .ExtractFromContent(content)
            .Select(ProxyEndPointParser.FromUrl)
            .ToArray();

        var autoUpdateOptions = ProxySettings.AutoUpdateOptions;
        await store.Merge(newProxyEndPoints, autoUpdateOptions.MaxItemCount, autoUpdateOptions.MaxPenalty,
            autoUpdateOptions.RemoveDuplicateIps, cancellationToken).Vhc();
        return newProxyEndPoints;
    }

    public async Task ResetStates(CancellationToken cancellationToken)
    {
        var store = await GetStore(cancellationToken).Vhc();
        await store.ResetStatuses(cancellationToken).Vhc();

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
            .GetHostCountries(proxyEndPointInfos.Select(x => x.EndPoint.Host), cancellationToken).Vhc();

        var store = await GetStore(cancellationToken).Vhc();
        foreach (var proxyEndPointInfo in proxyEndPointInfos) {
            var countryCode = hostCountries.GetValueOrDefault(proxyEndPointInfo.EndPoint.Host);
            if (countryCode != null)
                await store.SetCountryCode(proxyEndPointInfo.EndPoint.Id, countryCode, cancellationToken).Vhc();
        }
    }

    public async Task<ProxyOptions> GetProxyOptions(CancellationToken cancellationToken)
    {
        var resetStates = _pendingResetStates;
        _pendingResetStates = false;

        // this decides Managed vs None for a real connection, so read fresh from the store.
        // an auto-update url counts even with an empty store; the core downloads the initial list
        var hasCustomEndPoints = ProxySettings.Mode is AppProxyMode.Manual &&
                                 (await RefreshHasCustomEndPoints(cancellationToken).Vhc() ||
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

    private Task<IProxyEndPointStore> GetStore(CancellationToken cancellationToken)
    {
        lock (_storeLock)
            _storeTask ??= OpenStore();

        return _storeTask.WaitAsync(cancellationToken);
    }

    // one open for every caller, so no caller's token ends it
    private async Task<IProxyEndPointStore> OpenStore()
    {
        var store = new ProxyEndPointStore(dbPath);
        if (endPointCopy != null)
            await RestoreCopy(store, endPointCopy, CancellationToken.None).Vhc();

        return store;
    }

    // An empty db beside a copy is one the platform deleted while the app was closed: the proxies the
    // person added come back, their states to be learned again; a URL's come back with the URL.
    private static async Task RestoreCopy(IProxyEndPointStore store, ProxyEndPointCopy endPointCopy,
        CancellationToken cancellationToken)
    {
        if (await store.Count(cancellationToken).Vhc() > 0)
            return;

        var endPoints = endPointCopy.Load();
        if (endPoints.Count == 0)
            return;

        await store.Upsert(endPoints.Select(x => new ProxyEndPointRecord { EndPoint = x }).ToArray(),
            keepExistingStatus: true, keepExistingEnabled: false, cancellationToken).Vhc();
        VhLogger.Instance.LogInformation(
            "The proxies' database was empty. Restored the {Count} proxies the person added from their copy.",
            endPoints.Count);
    }

    // After a change the person made: the copy keeps the proxies the person added - those just added among
    // them - that the db still has, as the db has them.
    private async Task UpdateCopy(IProxyEndPointStore store, IReadOnlyList<string> addedIds,
        CancellationToken cancellationToken)
    {
        if (endPointCopy is null)
            return;

        var ids = endPointCopy.Load().Select(x => x.Id).Concat(addedIds).ToHashSet();
        var records = await store.List(cancellationToken).Vhc();
        endPointCopy.Save(records.Select(x => x.EndPoint).Where(x => ids.Contains(x.Id)).ToArray());
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
