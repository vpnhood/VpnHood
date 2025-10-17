using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using VpnHood.AppLib.Settings;
using VpnHood.Core.Client.Abstractions.ProxyNodes;
using VpnHood.Core.Client.VpnServices.Manager;
using VpnHood.Core.Common.IpLocations;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Services.Proxies;

public class AppProxyNodeService
{
    private readonly ServiceData _data;
    private readonly HostCountryResolver? _hostCountryResolver;
    private readonly string _infoFilePath;
    private readonly VpnServiceManager _vpnServiceManager;
    private readonly AppSettingsService _settingsService;
    private AppProxySettings ProxySettings => _settingsService.UserSettings.ProxySettings;

    public AppProxyNodeService(string storageFolder,
        IIpLocationProvider? ipLocationProvider,
        VpnServiceManager vpnServiceManager,
        AppSettingsService settingsService)
    {
        Directory.CreateDirectory(storageFolder);
        _infoFilePath = Path.Combine(storageFolder, "proxy_infos.json");
        _vpnServiceManager = vpnServiceManager;
        _settingsService = settingsService;
        _hostCountryResolver = ipLocationProvider != null ? new HostCountryResolver(ipLocationProvider) : null;
        _data = JsonUtils.TryDeserializeFile<ServiceData>(_infoFilePath) ?? new ServiceData();
    }

    public AppProxyNodeInfo[] GetNodeInfos()
    {
        var data = Update();
        return data.NodeInfos;
    }

    private ServiceData Update()
    {
        var connectionInfo = _vpnServiceManager.ConnectionInfo;
        var runtimeNodes = connectionInfo.SessionStatus?.ProxyManagerStatus.ProxyNodeInfos ?? [];

        // update from runtimeNodes
        if (connectionInfo.CreatedTime > _data.UpdateTime && !_vpnServiceManager.IsReconfiguring) {

            // overwrite Settings node if remote url list exists
            if (runtimeNodes.Any() &&
                ProxySettings.Mode is AppProxyMode.Custom &&
                ProxySettings.RemoteNotesUrl != null) {
                // remove NodeInfos that are not in runtimeNodes
                var runtimeNodeIds = runtimeNodes.Select(n => n.Node.Id);
                _data.NodeInfos = _data.NodeInfos.Where(n => runtimeNodeIds.Contains(n.Node.Id)).ToArray();

                // add NodeInfos that are in runtimeNodes but not in NodeInfos
                var existingNodeIds = _data.NodeInfos.Select(n => n.Node.Id);
                var newNodes = runtimeNodes
                    .Where(n => !existingNodeIds.Contains(n.Node.Id))
                    .Select(nodeInfo => new AppProxyNodeInfo {
                        Node = nodeInfo.Node,
                        CountryCode = null,
                        Status = nodeInfo.Status
                    });
                _data.NodeInfos = _data.NodeInfos.Concat(newNodes).ToArray();
            }

            // update status
            var nodeDict = _data.NodeInfos.ToDictionary(info => info.Node.Id, info => info);
            foreach (var runtimeNode in runtimeNodes) {
                if (nodeDict.TryGetValue(runtimeNode.Node.Id, out var existing))
                    existing.Status = runtimeNode.Status;
            }

            _data.ResetStates = false;
            Save();
        }

        return _data;
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

    public Task<AppProxyNodeInfo> Add(ProxyNode proxyNode)
    {
        proxyNode = ProxyNodeParser.Normalize(proxyNode);

        // update if already exists
        var existing = _data.NodeInfos.FirstOrDefault(n => n.Node.Id == proxyNode.Id);
        if (existing != null)
            return Update(proxyNode.Id, proxyNode);

        // add new node
        var newNodeInfo = new AppProxyNodeInfo {
            Node = proxyNode,
            CountryCode = null,
            Status = new ProxyNodeStatus()
        };
        _data.NodeInfos = _data.NodeInfos.Append(newNodeInfo).ToArray();

        Save();
        return Task.FromResult(newNodeInfo);
    }

    private void Save()
    {
        _data.UpdateTime = DateTime.Now; // make sure to use latest data
        _data.NodeInfos = _data.NodeInfos.DistinctBy(x => x.Node.Id).ToArray(); // remove duplicates

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

        File.WriteAllText(_infoFilePath, json);
        _settingsService.Save(); // fire changes
    }

    public Task Delete(string proxyNodeId)
    {
        _data.NodeInfos = _data.NodeInfos.Where(x => x.Node.Id != proxyNodeId).ToArray();
        Save();
        return Task.CompletedTask;
    }

    public Task<AppProxyNodeInfo> Update(string proxyNodeId, ProxyNode proxyNode)
    {
        proxyNode = ProxyNodeParser.Normalize(proxyNode);

        // replace the ProxyNode and keep its position. find the node by GetId
        var nodeInfo = _data.NodeInfos.Single(x => x.Node.Id == proxyNodeId);
        nodeInfo.Node = proxyNode;
        Save();

        var updatedNode = GetNodeInfos().Single(x => x.Node.Id == proxyNode.Id);
        return Task.FromResult(updatedNode);
    }

    public Task Import(string text, bool resetState)
    {
        throw new NotImplementedException();
    }

    public void ResetStates()
    {
        // remove from local state
        foreach (var nodeInfo in _data.NodeInfos)
            nodeInfo.Status = new ProxyNodeStatus();

        _data.ResetStates = true;
        Save();
    }

    private class ServiceData
    {
        public DateTime UpdateTime { get; set; } = DateTime.MinValue;
        public AppProxyNodeInfo[] NodeInfos { get; set; } = [];
        public bool ResetStates { get; set; }
    }

    public ProxyOptions GetProxyOptions()
    {
        var data = Update();
        return new ProxyOptions {
            ResetStates = data.ResetStates,
            ProxyNodes = data.NodeInfos
                .Select(x => x.Node)
                .ToArray()
        };
    }
}