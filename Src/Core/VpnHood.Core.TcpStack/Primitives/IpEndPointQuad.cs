using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.TcpStack.Primitives;

internal readonly record struct IpEndPointQuad(IpEndPointValue Source, IpEndPointValue Destination)
{
    public override string ToString() => $"{VhLogger.Format(Source.ToIPEndPoint())}->{VhLogger.Format(Destination.ToIPEndPoint())}";
}
            