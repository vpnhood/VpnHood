using WatsonWebserver.Core;
using HttpMethod = WatsonWebserver.Core.HttpMethod;

namespace VpnHood.AppLib.Api.WebHost.Helpers;

public interface IRouteMapper
{
    void AddStatic(HttpMethod method, string path, Func<HttpContextBase, Task> handler);
    void AddParam(HttpMethod method, string path, Func<HttpContextBase, Task> handler);
}