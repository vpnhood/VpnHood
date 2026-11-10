using VpnHood.Net.Quic.Abstractions;
using VpnHood.Net.Toolkit.Sockets;

namespace VpnHood.Core.Client;

/// <summary>
/// The VPN client's factory: the sockets it opens and, where the platform has QUIC, its QUIC clients.
/// </summary>
public interface IClientSocketFactory : ISocketFactory
{
    /// <summary>Gets the platform's QUIC, or null where it has none: the client then keeps to TCP.</summary>
    IQuicClientFactory? QuicClientFactory { get; }
}
