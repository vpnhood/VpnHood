using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.Sockets;

namespace VpnHood.Core.Client;

public class ClientUdpChannelOptions : UdpChannelOptions
{
    public ClientUdpChannelOptions()
    {
        base.LeaveTransmitterOpen = false; // always false for client channels
    }

    public new bool LeaveTransmitterOpen => base.LeaveTransmitterOpen; // always false
    public required byte[] ServerKey { get; init; }
    public required ISocketFactory SocketFactory { get; init; }
    public required TransferBufferSize? BufferSize { get; init; }
}