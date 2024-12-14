using System.Net;

namespace VpnHood.Core.Common.Net;

// ReSharper disable once InconsistentNaming
public class IPEndPointPair(IPEndPoint localEndPoint, IPEndPoint remoteEndPoint)
{
    public IPEndPoint LocalEndPoint { get; } = localEndPoint;
    public IPEndPoint RemoteEndPoint { get; } = remoteEndPoint;
}