using System.Net;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Net.Toolkit.Net;

// ReSharper disable once InconsistentNaming
/// <summary>Represents a connection's local and remote endpoints.</summary>
/// <param name="LocalEndPoint">The local endpoint.</param>
/// <param name="RemoteEndPoint">The remote endpoint.</param>
public readonly record struct IPEndPointPair(
    IPEndPoint LocalEndPoint,
    IPEndPoint RemoteEndPoint)
{
    /// <summary>Returns the pair as <c>local-&gt;remote</c>, redacted as <c>VhLogger.Format</c> redacts an endpoint.</summary>
    /// <returns>The text.</returns>
    public override string ToString() => $"{VhLogger.Format(LocalEndPoint)}->{VhLogger.Format(RemoteEndPoint)}";
}

