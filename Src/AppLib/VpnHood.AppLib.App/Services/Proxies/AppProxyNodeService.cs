using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Settings;
using VpnHood.Core.Client.Abstractions.ProxyNodes;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Manager;
using VpnHood.Core.Common.IpLocations;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Services.Proxies;

public class AppProxyNodeService
{
    private readonly ServiceData _data;
    private readonly HostCountryResolver? _hostCountryResolver;
    private readonly string _customInfoFilePath;
    private readonly IDeviceUiProvider _deviceUiProvider;
    private readonly VpnServiceManager _vpnServiceManager;
    private readonly AppSettingsService _settingsService;
    private AppProxySettings ProxySettings => _settingsService.UserSettings.ProxySettings;

    public AppProxyNodeService(string storageFolder,
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

    public bool IsProxyNodeActive {
        get {
            return ProxySettings.Mode switch {
                AppProxyMode.NoProxy => false,
                AppProxyMode.Device =>
                    _deviceUiProvider.IsProxySettingsSupported &&
                    _deviceUiProvider.GetProxySettings() != null,
                _ => _data.CustomNodeInfos.Any()
            };
        }
    }

    public AppProxyNodeInfo? GetDeviceProxy()
    {
        UpdateNodesByCore();
        return _data.SystemNodeInfo;
    }

    public AppProxyNodeInfo[] ListProxies()
    {
        UpdateNodesByCore();
        return _data.CustomNodeInfos;
    }

    public AppProxyNodeInfo Add(ProxyNode proxyNode)
    {
        var ret = AddInternal(proxyNode);
        Save();
        return ret;
    }

    private AppProxyNodeInfo AddInternal(ProxyNode proxyNode)
    {
        proxyNode = ProxyNodeParser.Normalize(proxyNode);

        // update if already exists
        var existing = _data.CustomNodeInfos.FirstOrDefault(n => n.Node.Id == proxyNode.Id);
        if (existing != null)
            return Update(proxyNode.Id, proxyNode);

        // add new node
        var newNodeInfo = new AppProxyNodeInfo {
            Node = proxyNode,
            CountryCode = null,
            Status = new ProxyNodeStatus()
        };
        _data.CustomNodeInfos = _data.CustomNodeInfos.Append(newNodeInfo).ToArray();
        return newNodeInfo;
    }


    public void Delete(string proxyNodeId)
    {
        _data.CustomNodeInfos = _data.CustomNodeInfos.Where(x => x.Node.Id != proxyNodeId).ToArray();
        Save();
    }

    public void DeleteAll()
    {
        _data.CustomNodeInfos = [];
        Save();
    }

    public AppProxyNodeInfo Update(string proxyNodeId, ProxyNode proxyNode)
    {
        proxyNode = ProxyNodeParser.Normalize(proxyNode);

        // replace the ProxyNode and keep its position. find the node by GetId
        var nodeInfo = _data.CustomNodeInfos.Single(x => x.Node.Id == proxyNodeId);
        nodeInfo.Node = proxyNode;
        Save();

        var updatedNode = ListProxies().Single(x => x.Node.Id == proxyNode.Id);
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
        var runtimeNodes = connectionInfo.ProxyManagerStatus?.ProxyNodeInfos ?? [];

        if (!_deviceUiProvider.IsProxySettingsSupported)
            return;

        var oldDeviceNode = _data.SystemNodeInfo?.Node;

        try {
            // update device proxy node 
            var deviceProxySettings = _deviceUiProvider.GetProxySettings();
            if (deviceProxySettings?.ProxyUrl is null) {
                _data.SystemNodeInfo = null;
                return;
            }

            // parse device proxy url
            var deviceProxyNode = VhUtils.TryInvoke("Parse device proxy url",
                () => ProxyNodeParser.FromUrl(deviceProxySettings.ProxyUrl));
            if (deviceProxyNode is null) {
                _data.SystemNodeInfo = null;
                return;
            }

            // ensure SystemNodeInfo exists
            _data.SystemNodeInfo ??= new AppProxyNodeInfo {
                Node = deviceProxyNode,
                CountryCode = null,
                Status = new ProxyNodeStatus()
            };

            // update node info
            _data.SystemNodeInfo.Node = deviceProxyNode;

            // update status
            if (ShouldUpdateNodesFromVpnService) {
                var runtimeNode = runtimeNodes.FirstOrDefault(n => n.Node.Id == deviceProxyNode.Id);
                if (runtimeNode != null)
                    _data.SystemNodeInfo.Status = runtimeNode.Status;
            }
        }
        finally {
            if (_data.SystemNodeInfo?.Node.Id != oldDeviceNode?.Id) {
                Save(updateSettings: true); // fire change if node changed
            }
        }
    }

    private void UpdateManualNodes()
    {
        if (!ShouldUpdateNodesFromVpnService ||
            ProxySettings.Mode is not AppProxyMode.Manual)
            return;

        var connectionInfo = _vpnServiceManager.ConnectionInfo;
        var runtimeNodes = connectionInfo.ProxyManagerStatus?.ProxyNodeInfos ?? [];

        // overwrite Settings node if remote url list exists
        if (runtimeNodes.Any() &&
            ProxySettings.Mode is AppProxyMode.Manual &&
            ProxySettings.AutoUpdateListUrl != null &&
            connectionInfo.ProxyManagerStatus?.AutoUpdate is true) {
            // remove NodeInfos that are not in runtimeNodes
            var runtimeNodeIds = runtimeNodes.Select(n => n.Node.Id);
            _data.CustomNodeInfos = _data.CustomNodeInfos.Where(n => runtimeNodeIds.Contains(n.Node.Id)).ToArray();

            // add NodeInfos that are in runtimeNodes but not in NodeInfos
            var existingNodeIds = _data.CustomNodeInfos.Select(n => n.Node.Id);
            var newNodes = runtimeNodes
                .Where(n => !existingNodeIds.Contains(n.Node.Id))
                .Select(nodeInfo => new AppProxyNodeInfo {
                    Node = nodeInfo.Node,
                    CountryCode = null,
                    Status = nodeInfo.Status
                });

            _data.CustomNodeInfos = _data.CustomNodeInfos.Concat(newNodes).ToArray();
        }

        // update status
        var nodeDict = _data.CustomNodeInfos.ToDictionary(info => info.Node.Id, info => info);
        foreach (var runtimeNode in runtimeNodes) {
            if (nodeDict.TryGetValue(runtimeNode.Node.Id, out var existing))
                existing.Status = runtimeNode.Status;
        }
    }

    // ReSharper disable once UnusedMember.Local
    private async Task UpdateCountryCode(IEnumerable<AppProxyNodeInfo> proxyNodeInfos, CancellationToken cancellationToken)
    {
        if (_hostCountryResolver is null)
            return;

        // resolve all host with domain name 
        proxyNodeInfos = proxyNodeInfos.Where(x => x.CountryCode is null).ToArray();
        var hostCountries = await _hostCountryResolver
            .GetHostCountries(proxyNodeInfos.Select(x => x.Node.Host), cancellationToken);

        foreach (var proxyNodeInfo in proxyNodeInfos)
            proxyNodeInfo.CountryCode = hostCountries.GetValueOrDefault(proxyNodeInfo.Node.Host);
    }

    private bool ShouldUpdateNodesFromVpnService =>
        _vpnServiceManager.ConnectionInfo.CreatedTime > _data.UpdateTime &&
        !_vpnServiceManager.IsReconfiguring;

    public void Import(string text)
    {
        var proxyNodeUrls = ProxyNodeParser.ExtractFromContent(text);
        var proxyNodes = proxyNodeUrls.Select(ProxyNodeParser.FromUrl);
        foreach (var proxyNode in proxyNodes)
            Add(proxyNode);

        Save();
    }

    public void ResetStates()
    {
        // remove from local state
        foreach (var nodeInfo in _data.CustomNodeInfos)
            nodeInfo.Status = new ProxyNodeStatus();

        // reset system proxy node status
        if (_data.SystemNodeInfo != null)
            _data.SystemNodeInfo.Status = new ProxyNodeStatus();

        _data.ResetStates = true;
        Save();
    }

    private void Save(bool updateSettings = true)
    {
        _data.UpdateTime = DateTime.Now; // make sure to use latest data
        _data.CustomNodeInfos = _data.CustomNodeInfos.DistinctBy(x => x.Node.Id).ToArray(); // remove duplicates

        // customize serialization to ignore Url and Id properties
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(typeInfo => {
            if (typeInfo.Type == typeof(ProxyNode)) {
                var prop = typeInfo.Properties.FirstOrDefault(p => p.Name == nameof(ProxyNode.Url));
                if (prop is not null)
                    typeInfo.Properties.Remove(prop);

                prop = typeInfo.Properties.FirstOrDefault(p => p.Name == nameof(ProxyNode.Id));
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

        var proxyNodes = ProxySettings.Mode switch {
            AppProxyMode.NoProxy => [],
            AppProxyMode.Device => _data.SystemNodeInfo != null ? [_data.SystemNodeInfo.Node] : [],
            AppProxyMode.Manual => _data.CustomNodeInfos.Select(x => x.Node).ToArray(),
            _ => []
        };

        return new ProxyOptions {
            ResetStates = _data.ResetStates,
            ProxyNodes = proxyNodes
        };
    }

    private class ServiceData
    {
        public DateTime UpdateTime { get; set; } = DateTime.MinValue;
        public AppProxyNodeInfo[] CustomNodeInfos { get; set; } = [];
        public AppProxyNodeInfo? SystemNodeInfo { get; set; }
        public bool ResetStates { get; set; }
    }
}