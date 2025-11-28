using WatsonWebserver.Lite;

namespace VpnHood.AppLib.WebServer.Helpers;

public static class WebServerLiteExtension
{
    extension (WebserverLite server)
    {
        public ApiRouteMapper AddRouteMapper(bool isDebugMode)
        {
            return new ApiRouteMapper(server, isDebugMode);
        }
    }
}