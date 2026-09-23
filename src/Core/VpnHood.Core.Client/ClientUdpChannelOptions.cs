using System.Net;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Tunneling.Channels;

namespace VpnHood.Core.Client;

public class ClientUdpChannelOptions : UdpChannelOptions
{
    public required IPEndPoint RemoteEndPoint { get; init; }
    public required ulong SessionId { get; init; }
    public required byte[] SessionKey { get; init; }
    public required TransferBufferSize? BufferSize { get; init; }
}