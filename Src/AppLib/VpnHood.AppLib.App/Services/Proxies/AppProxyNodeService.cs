using System.Net;
using VpnHood.AppLib.Settings;
using VpnHood.Core.Client.Abstractions.ProxyNodes;
using VpnHood.Core.Client.VpnServices.Manager;
using VpnHood.Core.Common.IpLocations;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Services.Proxies;

public class AppProxyNodeService(
    string storageFolder,
    IIpLocationProvider ipLocationProvider,
    VpnServiceManager vpnServiceManager,
    AppSettingsService settingsService)
{
    private readonly string _infoFilePath = Path.Combine(storageFolder, "proxy_infos.json");
    private AppProxyNodeInfo[]? _proxyNodeInfos;
    private AppProxySettings ProxySettings => settingsService.UserSettings.ProxySettings;

    public Task<AppProxyNodeInfo[]> GetProxyNodeInfos(CancellationToken cancellationToken)
    {
        Update();
        //UpdateCountryCode(_proxyNodeInfos!, cancellationToken); //may be too slow
        return Task.FromResult(_proxyNodeInfos!);
    }

    public void Update()
    {
        var runtimeNodes = vpnServiceManager.ConnectionInfo.SessionStatus?.ProxyManagerStatus.ProxyNodeInfos ?? [];

        // overwrite Settings node if remote url list exists
        if (runtimeNodes.Any() &&
            ProxySettings.Mode is AppProxyMode.Custom &&
            ProxySettings.RemoteNotesUrl != null)
            ProxySettings.Nodes = runtimeNodes.Select(x => x.Node).ToArray();

        // load last node states and sync it with user settings
        _proxyNodeInfos ??= JsonUtils.TryDeserializeFile<AppProxyNodeInfo[]>(_infoFilePath) ?? [];
        _proxyNodeInfos = SyncNodeInfosWithNodes(
            _proxyNodeInfos, settingsService.UserSettings.ProxySettings.Nodes).ToArray();

        // update _proxyNodeInfos status from runtimeNodes status
        var nodeDict = _proxyNodeInfos.ToDictionary(info => info.Node.GetId(), info => info);
        foreach (var runtimeNode in runtimeNodes) if (nodeDict.TryGetValue(runtimeNode.Node.GetId(), out var existing))
                existing.Status = runtimeNode.Status;
    }

    // ReSharper disable once UnusedMember.Local
    private Task UpdateCountryCode(IEnumerable<AppProxyNodeInfo> proxyNodeInfos, CancellationToken cancellationToken)
    {
        var tasks = proxyNodeInfos
            .Where(x => x.CountryCode == null)
            .Select(async info => {
                try {
                    var ipAddress = info.Status.IpAddress;
                    if (ipAddress is null && IPAddress.TryParse(info.Node.Host, out var hostIpAddress))
                        info.Status.IpAddress = hostIpAddress;

                    if (ipAddress is not null) {
                        var location = await ipLocationProvider
                            .GetLocation(ipAddress, cancellationToken)
                            .Vhc();
                        info.CountryCode = location.CountryCode;
                    }
                }
                catch {
                    info.CountryCode = null;
                }
            });

        return Task.WhenAll(tasks);
    }

    private static IEnumerable<AppProxyNodeInfo> SyncNodeInfosWithNodes(
        IEnumerable<AppProxyNodeInfo> nodeInfos, ProxyNode[] nodes)
    {
        // Pseudocode:
        // - Build a dictionary keyed by node id from existing infos.
        // - Iterate 'nodes' in the given order:
        //   - If an info exists, reuse it and update its Node reference to the current node.
        //   - If not, create a new AppProxyNodeInfo with default status and null CountryCode.
        // - Return the list in the same order as 'nodes'. Deleted nodes are naturally excluded.
        var nodeDict = nodeInfos.ToDictionary(info => info.Node.GetId(), info => info);

        var orderedInfos = new List<AppProxyNodeInfo>(nodes.Length);
        foreach (var node in nodes) {
            var id = node.GetId();
            if (nodeDict.TryGetValue(id, out var existing)) orderedInfos.Add(new AppProxyNodeInfo(node) {
                Status = existing.Status,
                CountryCode = null
            });
            else orderedInfos.Add(new AppProxyNodeInfo(node) {
                CountryCode = null,
            });
        }

        return orderedInfos;
    }

}