using VpnHood.AppLib.Services.Proxies;
using VpnHood.Core.Proxies.EndPointManagement.Abstractions;

namespace VpnHood.AppLib.WebServer.Api;

public interface IProxyEndPointController
{
    Task<AppProxyEndPointInfo> Get(string proxyEndPointId);
    Task<AppProxyEndPointInfo> Add(ProxyEndPoint proxyEndPoint);
    Task<AppProxyEndPointInfo> Update(string proxyEndPointId, ProxyEndPoint proxyEndPoint);
    Task Delete(string proxyEndPointId);
    Task DeleteAll();
    Task ResetState();
    Task<AppProxyEndPointInfo?> GetDevice();
    Task<AppProxyEndPointInfo[]> List();
    Task Import(string content);
    Task ReloadUrl(CancellationToken cancellationToken);
    Task<AppProxyEndPointInfo> Parse(string text, ProxyEndPointDefaults defaults);
}

