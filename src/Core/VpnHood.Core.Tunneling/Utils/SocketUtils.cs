using System.Net.Sockets;

namespace VpnHood.Core.Tunneling.Utils;

public static class SocketUtils
{
    public static bool IsInvalidUdpStateException(Exception ex)
    {
        // On IPv6, InvalidArgument can happen for bad destination/packet conditions without the socket actually being dead
        // SocketError.InvalidArgument

        // Returns TRUE if the client is useless/dead
        return ex is ObjectDisposedException or SocketException {
            SocketErrorCode:
            SocketError.OperationAborted or // Socket closed during async op
            SocketError.Interrupted or      // Socket closed during blocking op
            SocketError.NotSocket or        // Handle is no longer a valid socket
            SocketError.ConnectionAborted   // Local network stack killed it
        };
    }
}