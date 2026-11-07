using VpnHood.Core.Tunneling.Channels.Streams;
using VpnHood.Core.Tunneling.Connections;

namespace VpnHood.Core.Server;

internal static class ServerInitialStreamPadding
{
    // Hello can use a plain POST; only later forwarding streams spend the advertised budget.
    public static bool ShouldAdvertise(Session session, IStreamConnection connection) =>
        session.StartPadding && connection is ServerStreamConnection { HttpProtocolVersion: >= 15 };

    // The request alone fixes framing: proxies need not preserve custom response headers.
    public static bool UsesStandardMasking(int protocolVersion, IReadOnlyDictionary<string, string> headers) =>
        protocolVersion >= 15 &&
        bool.TryParse(headers.GetValueOrDefault("X-WebSocketMasking"), out var masking) && masking;

    public static void StartForwarding(Session session, Stream stream)
    {
        // Reuse preserves the stream's agreement, but drops the initial HTTP connection wrapper.
        if (session.StartPadding && stream is WebSocketStream { UsesStandardMasking: true } webSocketStream)
            webSocketStream.EnableInitialPadding();
    }
}
