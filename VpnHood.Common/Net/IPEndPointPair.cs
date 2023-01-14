using System.Net;

namespace VpnHood.Common.Net;

// ReSharper disable once InconsistentNaming
public class IPEndPointPair
{
    public IPEndPointPair(IPEndPoint localEndPoint, IPEndPoint remoteEndPoint)
    {
        LocalEndPoint = localEndPoint;
        RemoteEndPoint = remoteEndPoint;
    }

    public IPEndPoint LocalEndPoint { get; } 
    public IPEndPoint RemoteEndPoint { get; } 
}