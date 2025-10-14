using VpnHood.AppLib.Services.Proxies;
using VpnHood.Core.Client.Abstractions.ProxyNodes;

namespace VpnHood.AppLib.WebServer.Api;

public interface IProxyNodeController
{
    Task<AppProxyNodeInfo> Parse(string text, ProxyNodeDefaults defaults);
    Task<AppProxyNodeInfo> Add(ProxyNode proxyNode);
    Task<AppProxyNodeInfo> Update(Uri url, ProxyNode proxyNode);
    Task Delete(Uri url);
    Task<AppProxyNodeInfo[]> List();
    Task Import(string text, bool removeOld);
}

