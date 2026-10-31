using VpnHood.Net.Http3;

namespace VpnHood.Core.Server.Listeners;

internal static class Http3ServerOptionsFactory
{
    public static Http3ServerOptions Create() => new() {
        ResponseHeaders = Http3UnauthorizedResponder.CreateHeaders
    };
}
