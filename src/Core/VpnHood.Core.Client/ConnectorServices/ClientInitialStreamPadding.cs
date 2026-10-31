using VpnHood.Core.Tunneling.Channels.Streams;
using VpnHood.Core.Tunneling.Messaging;

namespace VpnHood.Core.Client.ConnectorServices;

internal sealed class ClientInitialStreamPadding
{
    private bool _enabled;

    public void Enable(bool enabled) => _enabled = enabled;

    public bool OffersMasking(bool isTcpPacketChannel) => _enabled && !isTcpPacketChannel;

    public void Start(Stream stream, RequestCode requestCode)
    {
        // Reuse retains the transport's agreement, while each logical proxy stream gets a fresh budget.
        if (_enabled && requestCode == RequestCode.ProxyChannel &&
            stream is WebSocketStream { UsesStandardMasking: true } webSocketStream)
            webSocketStream.EnableInitialPadding();
    }
}
