using System.Net.Sockets;

namespace VpnHood.Core.Tunneling.Utils;

public static class SocketUtils
{
    public static bool IsInvalidUdpStateException(Exception ex)
    {
        // Returns TRUE if the client is useless/dead
        return ex is ObjectDisposedException ||
               ex is SocketException {
                   SocketErrorCode:
                   SocketError.OperationAborted or // Socket closed during async op
                   SocketError.Interrupted or      // Socket closed during blocking op
                   SocketError.NotSocket or        // Handle is no longer a valid socket
                   SocketError.InvalidArgument or  // Often thrown if handle is already closed
                   SocketError.ConnectionAborted   // Local network stack killed it
               };
    }
}