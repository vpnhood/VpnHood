using WatsonWebserver.Core;
using HttpMethod = WatsonWebserver.Core.HttpMethod;

namespace VpnHood.AppLib.WebServer;

public interface IRouteMapper
{
    void AddStatic(HttpMethod method, string path, Func<HttpContextBase, Task> handler);
    void AddParam(HttpMethod method, string path, Func<HttpContextBase, Task> handler);
}