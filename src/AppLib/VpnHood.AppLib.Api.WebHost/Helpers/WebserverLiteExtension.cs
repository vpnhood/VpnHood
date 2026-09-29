using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;
using WatsonWebserver.Core;
using WatsonWebserver.Lite;

namespace VpnHood.AppLib.Api.WebHost.Helpers;

public static class WebServerLiteExtension
{
    extension(WebserverLite server)
    {
        public ApiRouteMapper AddRouteMapper(bool allowAnyOrigin, Func<HttpContextBase, Task<bool>>? guard = null)
        {
            return new ApiRouteMapper(server, allowAnyOrigin, guard);
        }

        public void TryStop()
        {
            try {
                if (server.IsListening)
                    server.Stop();
            }
            catch (Exception ex) {
                VhLogger.Instance.LogWarning(ex, "Could not stop the WebserverLite.");
            }
        }
    }
}