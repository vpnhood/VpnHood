using System.Net;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.Core.Toolkit.Net;

// ReSharper disable once InconsistentNaming
public readonly record struct IPEndPointPair(
    IPEndPoint LocalEndPoint,
    IPEndPoint RemoteEndPoint)
{
    public override string ToString() => $"{VhLogger.Format(LocalEndPoint)}->{VhLogger.Format(RemoteEndPoint)}";
}