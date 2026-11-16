using System.Net;
using System.Net.Sockets;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Net.Toolkit.Net;

/// <summary>
/// Provides a TCP connection over one address family, whatever the OS prefers, for an HTTP handler's
/// <c>ConnectCallback</c>, so the web service sees the caller's address of that family.
/// </summary>
public static class AddressFamilyConnector
{
    /// <summary>Connects to a host over an address family.</summary>
    /// <param name="endPoint">The host and port.</param>
    /// <param name="addressFamily">The address family.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is a stream that owns the connection's socket.</returns>
    /// <exception cref="SocketException">The connection failed, as when the host has no address of the family.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static async ValueTask<Stream> Connect(DnsEndPoint endPoint, AddressFamily addressFamily,
        CancellationToken cancellationToken)
    {
        var socket = new Socket(addressFamily, SocketType.Stream, ProtocolType.Tcp);
        try {
            await socket.ConnectAsync(endPoint, cancellationToken).Vhc();
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch {
            socket.Dispose();
            throw;
        }
    }
}
