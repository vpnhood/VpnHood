using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Settings;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Manager;
using VpnHood.Core.Common.IpLocations;
using VpnHood.Core.Proxies.EndPointManagement.Abstractions;
using VpnHood.Core.Proxies.EndPointManagement.Abstractions.Options;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Services.Proxies;

public class AppProxyEndPointService
{
    private readonly ServiceData _data;
    private readonly HostCountryResolver? _hostCountryResolver;
    private readonly string _customInfoFilePath;
    private readonly IDeviceUiProvider _deviceUiProvider;
    private readonly VpnServiceManager _vpnServiceManager;
    private readonly AppSettingsService _settingsService;
    private AppProxySettings ProxySettings => _settingsService.UserSettings.ProxySettings;

    public AppProxyEndPointService(string storageFolder,
        IIpLocationProvider? ipLocationProvider,
        IDeviceUiProvider deviceUiProvider,
        VpnServiceManager vpnServiceManager,
        AppSettingsService settingsService)
    {
        Directory.CreateDirectory(storageFolder);
        _customInfoFilePath = Path.Combine(storageFolder, "proxy_infos.json");
        _deviceUiProvider = deviceUiProvider;
        _vpnServiceManager = vpnServiceManager;
        _settingsService = settingsService;
        _hostCountryResolver = ipLocationProvider != null ? new HostCountryResolver(ipLocationProvider) : null;
        _data = JsonUtils.TryDeserializeFile<ServiceData>(_customInfoFilePath) ?? new ServiceData();
        vpnServiceManager.Reconfigured += OnVpnServiceManagerOnReconfigured;
    }

    public bool IsProxyEndPointActive {
        get {
            return ProxySettings.Mode switch {
                AppProxyMode.NoProxy => false,
                AppProxyMode.Device =>
                    _deviceUiProvider.IsProxySettingsSupported &&
                    _deviceUiProvider.GetProxySettings() != null,
                _ => _data.CustomEndPointInfos.Any()
            };
        }
    }

    public AppProxyEndPointInfo? GetDeviceProxy()
    {
        UpdateNodesByCore();
        return _data.SystemNodeInfo;
    }

    public AppProxyEndPointInfo[] ListProxies()
    {
        UpdateNodesByCore();
        var res = _data.CustomEndPointInfos
            .OrderBy(x => x.Status.Quality);

        return res.ToArray();
    }

    public AppProxyEndPointInfo Get(string id)
    {
        var res = _data.CustomEndPointInfos.Single(x => x.EndPoint.Id == id);
        return res;
    }

    public AppProxyEndPointInfo Add(ProxyEndPoint proxyEndPoint)
    {
        var ret = AddInternal(proxyEndPoint);
        Save();
        return ret;
    }

    public AppProxyEndPointInfo Update(string id, ProxyEndPoint proxyEndPoint)
    {
        var res = UpdateInternal(id, proxyEndPoint);
        Save();
        return res;
    }

    public void Delete(string proxyEndPointId)
    {
        _data.CustomEndPointInfos = _data.CustomEndPointInfos.Where(x => x.EndPoint.Id != proxyEndPointId).ToArray();
        Save();
    }

    public void DeleteAll()
    {
        _data.CustomEndPointInfos = [];
        Save();
    }

    private AppProxyEndPointInfo AddInternal(ProxyEndPoint proxyEndPoint)
    {
        proxyEndPoint = ProxyEndPointParser.Normalize(proxyEndPoint);

        // update if already exists
        var existing = _data.CustomEndPointInfos.FirstOrDefault(n => n.EndPoint.Id == proxyEndPoint.Id);
        if (existing != null)
            return UpdateInternal(proxyEndPoint.Id, proxyEndPoint);

        // add a new endpoint
        var newNodeInfo = new AppProxyEndPointInfo {
            EndPoint = proxyEndPoint,
            CountryCode = null,
            Status = new ProxyEndPointStatus()
        };
        _data.CustomEndPointInfos = _data.CustomEndPointInfos.Append(newNodeInfo).ToArray();
        return newNodeInfo;
    }

    private AppProxyEndPointInfo UpdateInternal(string proxyEndPointId, ProxyEndPoint proxyEndPoint)
    {
        proxyEndPoint = ProxyEndPointParser.Normalize(proxyEndPoint);

        // replace the ProxyEndPoint and keep its position. find the endpoint by GetId
        var endPointInfo = _data.CustomEndPointInfos.Single(x => x.EndPoint.Id == proxyEndPointId);
        endPointInfo.EndPoint = proxyEndPoint;

        // return updated node
        var updatedNode = ListProxies().Single(x => x.EndPoint.Id == proxyEndPoint.Id);
        return updatedNode;
    }

    private void UpdateNodesByCore()
    {
        UpdateManualNodes();
        UpdateSystemNode();

        // save if updated from vpn service
        if (ShouldUpdateNodesFromVpnService)
            Save(updateSettings: false);
    }


    private void UpdateSystemNode()
    {
        var connectionInfo = _vpnServiceManager.ConnectionInfo;
        var runtimeNodes = connectionInfo.ProxyManagerStatus?.ProxyEndPointInfos ?? [];

        if (!_deviceUiProvider.IsProxySettingsSupported)
            return;

        var oldDeviceEndPoint = _data.SystemNodeInfo?.EndPoint;

        try {
            // update device proxy endpoint 
            var deviceProxySettings = _deviceUiProvider.GetProxySettings();
            if (deviceProxySettings?.ProxyUrl is null) {
                _data.SystemNodeInfo = null;
                return;
            }

            // parse device proxy url
            var deviceProxyEndPoint = VhUtils.TryInvoke("Parse device proxy url",
                () => ProxyEndPointParser.FromUrl(deviceProxySettings.ProxyUrl));
            if (deviceProxyEndPoint is null) {
                _data.SystemNodeInfo = null;
                return;
            }

            // ensure SystemNodeInfo exists
            _data.SystemNodeInfo ??= new AppProxyEndPointInfo {
                EndPoint = deviceProxyEndPoint,
                CountryCode = null,
                Status = new ProxyEndPointStatus()
            };

            // update endpoint info
            _data.SystemNodeInfo.EndPoint = deviceProxyEndPoint;

            // update status
            if (ShouldUpdateNodesFromVpnService) {
                var runtimeNode = runtimeNodes.FirstOrDefault(n => n.EndPoint.Id == deviceProxyEndPoint.Id);
                if (runtimeNode != null)
                    _data.SystemNodeInfo.Status = runtimeNode.Status;
            }
        }
        finally {
            if (_data.SystemNodeInfo?.EndPoint.Id != oldDeviceEndPoint?.Id) {
                Save(updateSettings: true); // fire change if endpoint changed
            }
        }
    }

    private void UpdateManualNodes()
    {
        if (!ShouldUpdateNodesFromVpnService ||
            ProxySettings.Mode is not AppProxyMode.Manual)
            return;

        var connectionInfo = _vpnServiceManager.ConnectionInfo;
        var runtimeNodes = connectionInfo.ProxyManagerStatus?.ProxyEndPointInfos ?? [];

        // determine if we should override nodes
        var overrideNodes =
            runtimeNodes.Any() &&
            ProxySettings.AutoUpdateOptions.Url != null &&
            connectionInfo.ProxyManagerStatus?.AutoUpdate is true;

        // Remove old nodes that do not exist in runtime nodes
        if (overrideNodes) {
            // remove NodeInfos that are not in runtimeEndpoints
            var runtimeNodeIds = runtimeNodes.Select(n => n.EndPoint.Id);
            _data.CustomEndPointInfos = _data.CustomEndPointInfos.Where(n => runtimeNodeIds.Contains(n.EndPoint.Id)).ToArray();

            // add NodeInfos that are in runtimeNodes but not in EndPointInfos
            var existingNodeIds = _data.CustomEndPointInfos.Select(n => n.EndPoint.Id);
            var newNodes = runtimeNodes
                .Where(n => !existingNodeIds.Contains(n.EndPoint.Id))
                .Select(endPointInfo => new AppProxyEndPointInfo {
                    EndPoint = endPointInfo.EndPoint,
                    CountryCode = null,
                    Status = endPointInfo.Status
                });

            _data.CustomEndPointInfos = _data.CustomEndPointInfos.Concat(newNodes).ToArray();
        }

        // update status
        var endpointDict = _data.CustomEndPointInfos.ToDictionary(info => info.EndPoint.Id, info => info);
        foreach (var runtimeNode in runtimeNodes) {
            if (endpointDict.TryGetValue(runtimeNode.EndPoint.Id, out var existing)) {
                existing.Status = runtimeNode.Status;
                existing.EndPoint.IsEnabled = runtimeNode.EndPoint.IsEnabled;
            }
        }
    }

    // ReSharper disable once UnusedMember.Local
    private async Task UpdateCountryCode(IEnumerable<AppProxyEndPointInfo> proxyEndPointInfos, CancellationToken cancellationToken)
    {
        if (_hostCountryResolver is null)
            return;

        // resolve all host with domain name 
        proxyEndPointInfos = proxyEndPointInfos.Where(x => x.CountryCode is null).ToArray();
        var hostCountries = await _hostCountryResolver
            .GetHostCountries(proxyEndPointInfos.Select(x => x.EndPoint.Host), cancellationToken);

        foreach (var proxyEndPointInfo in proxyEndPointInfos)
            proxyEndPointInfo.CountryCode = hostCountries.GetValueOrDefault(proxyEndPointInfo.EndPoint.Host);
    }

    private bool ShouldUpdateNodesFromVpnService =>
        _vpnServiceManager.ConnectionInfo.
        _vpnServiceManager.ConnectionInfo.CreatedTime > _data.UpdateTime &&
        !_vpnServiceManager.IsReconfiguring;

    public void Import(string content)
    {
        // parse new endpoints
        var newProxyEndPoints = ProxyEndPointParser
            .ExtractFromContent(content)
            .Select(ProxyEndPointParser.FromUrl)
            .ToArray();

        // merge with existing endpoints
        var endpoints = ProxyEndPointUpdater.Merge(_data.CustomEndPointInfos, newProxyEndPoints,
            ProxySettings.AutoUpdateOptions.MaxItemCount, ProxySettings.AutoUpdateOptions.MaxPenalty);

        // remove _data.CustomEndPointInfos that does not exist in endpoints
        var endpointIds = endpoints.Select(e => e.Id).ToHashSet();
        _data.CustomEndPointInfos = _data.CustomEndPointInfos
            .Where(info => endpointIds.Contains(info.EndPoint.Id))
            .ToArray();

        // add new endpoints
        var newEndPointInfos = endpoints.Select(x => new AppProxyEndPointInfo {
            EndPoint = x,
            CountryCode = null,
            Status = new ProxyEndPointStatus()
        });
        _data.CustomEndPointInfos = _data.CustomEndPointInfos.Concat(newEndPointInfos).ToArray();

        // save changes
        Save();
    }

    public async Task ReloadUrl(CancellationToken cancellationToken)
    {
        // fetch endpoints from url and merge
        using var httpClient = new HttpClient();
        var content = await httpClient.GetStringAsync(ProxySettings.AutoUpdateOptions.Url, cancellationToken);
        Import(content);
    }

    public void ResetStates()
    {
        // remove from local state
        foreach (var endPointInfo in _data.CustomEndPointInfos)
            endPointInfo.Status = new ProxyEndPointStatus();

        // reset system proxy endpoint status
        if (_data.SystemNodeInfo != null)
            _data.SystemNodeInfo.Status = new ProxyEndPointStatus();

        _data.ResetStates = true;
        Save();
    }

    private void Save(bool updateSettings = true)
    {
        _data.UpdateTime = DateTime.Now; // make sure to use latest data
        _data.CustomEndPointInfos = _data.CustomEndPointInfos.DistinctBy(x => x.EndPoint.Id).ToArray(); // remove duplicates

        // customize serialization to ignore Url and Id properties
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(typeInfo => {
            if (typeInfo.Type == typeof(ProxyEndPoint)) {
                var prop = typeInfo.Properties.FirstOrDefault(p => p.Name == nameof(ProxyEndPoint.Url));
                if (prop is not null)
                    typeInfo.Properties.Remove(prop);

                prop = typeInfo.Properties.FirstOrDefault(p => p.Name == nameof(ProxyEndPoint.Id));
                if (prop is not null)
                    typeInfo.Properties.Remove(prop);
            }
        });

        // serialize to file
        var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions {
            WriteIndented = true,
            TypeInfoResolver = resolver
        });

        File.WriteAllText(_customInfoFilePath, json);

        if (updateSettings)
            _settingsService.Save(); // fire changes
    }

    private void OnVpnServiceManagerOnReconfigured(object? sender,
        ClientReconfigureParams clientReconfigureParams)
    {
        if (clientReconfigureParams.ProxyOptions.ResetStates)
            _data.ResetStates = false;
    }

    public ProxyOptions GetProxyOptions()
    {
        UpdateNodesByCore();

        var proxyEndPoints = ProxySettings.Mode switch {
            AppProxyMode.NoProxy => [],
            AppProxyMode.Device => _data.SystemNodeInfo != null ? [_data.SystemNodeInfo.EndPoint] : [],
            AppProxyMode.Manual => _data.CustomEndPointInfos.Select(x => x.EndPoint).ToArray(),
            _ => []
        };

        return new ProxyOptions {
            ResetStates = _data.ResetStates,
            ProxyEndPoints = proxyEndPoints,
            AutoUpdateOptions = ProxySettings.AutoUpdateOptions
        };
    }

    private class ServiceData
    {
        public DateTime UpdateTime { get; set; } = DateTime.MinValue;
        public AppProxyEndPointInfo[] CustomEndPointInfos { get; set; } = [];
        public AppProxyEndPointInfo? SystemNodeInfo { get; set; }
        public bool ResetStates { get; set; }
    }
}