using System.Net;
using System.Net.Sockets;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Net.Toolkit.Net;

// A connection over one address family, whatever the OS prefers, for an HTTP handler's ConnectCallback:
// the web service then sees the caller's address of that family.
public static class AddressFamilyConnector
{
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
