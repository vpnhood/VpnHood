using VpnHood.Core.Client.Abstractions.ProxyNodes;

namespace VpnHood.AppLib.WebServer.Api;

public interface IProxyNodeController
{
    Task<ProxyNodeInfo[]> List();
    Task<ProxyNodeInfo> Parse(string url, ProxyNodeDefaults defaults);
    Task<ProxyNodeInfo> Update(string url, ProxyNode proxyNode);
    Task<ProxyNodeInfo> UpdateUrls(string urls, bool removeOld);
}

