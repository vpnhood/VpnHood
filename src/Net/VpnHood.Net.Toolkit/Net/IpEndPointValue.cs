using System.Net;

namespace VpnHood.Net.Toolkit.Net;

public readonly record struct IpEndPointValue(IPAddress Address, int Port)
{
    // ReSharper disable once InconsistentNaming
    public IPEndPoint ToIPEndPoint() => new(Address, Port);
}