using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Toolkit.Sockets;
using VpnHood.Core.VpnAdapters.Abstractions;

namespace VpnHood.Core.Client.VpnServices.Host;

/// <summary>
/// What the host prepared for a single connection, handed to <see cref="VpnHoodClientFactory.Create"/>.
/// The adapter arrives ready-made (the null-capture decision is host policy); everything path- or
/// store-backed stays raw in <see cref="ServiceOptions"/> so the factory — default or derived — resolves it.
/// </summary>
public class VpnHoodClientParams
{
    public required VpnServiceOptions ServiceOptions { get; init; }
    public required IVpnAdapter VpnAdapter { get; init; }
    public required ISocketFactory SocketFactory { get; init; }
    public required string ConfigFolder { get; init; }
}
