using System.Net;
using PacketDotNet;

namespace VpnHood.Tunneling;

public class EndPointEventArgs
{
    public ProtocolType ProtocolType { get; }
    public IPEndPoint RemoteEndPoint { get; }

    public EndPointEventArgs(ProtocolType protocolType, IPEndPoint remoteEndPoint)
    {
        RemoteEndPoint = remoteEndPoint;
        ProtocolType = protocolType;
    }
}