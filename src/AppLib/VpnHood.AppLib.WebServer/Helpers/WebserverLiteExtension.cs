using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;
using WatsonWebserver.Lite;

namespace VpnHood.AppLib.WebServer.Helpers;

public static class WebServerLiteExtension
{
    extension(WebserverLite server)
    {
        public ApiRouteMapper AddRouteMapper(bool isDebugMode)
        {
            return new ApiRouteMapper(server, isDebugMode);
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