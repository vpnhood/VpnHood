using System.Net;
using PacketDotNet;

namespace VpnHood.Tunneling;

public class EndPointEventArgs
{
    public ProtocolType ProtocolType { get; }
    public int LocalPort { get; }
    public IPEndPoint DestinationEndPoint { get; }

    public EndPointEventArgs(ProtocolType protocolType, int localPort, IPEndPoint destinationEndPoint)
    {
        ProtocolType = protocolType;
        LocalPort = localPort;
        DestinationEndPoint = destinationEndPoint;
    }
}