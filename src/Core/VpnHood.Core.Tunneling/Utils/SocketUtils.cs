using System.Net.Sockets;

namespace VpnHood.Core.Tunneling.Utils;

public static class SocketUtils
{
    public static bool IsInvalidUdpStateException(Exception ex)
    {
        // On IPv6, InvalidArgument can happen for bad destination/packet conditions without the socket actually being dead
        // SocketError.InvalidArgument

        // ObjectDisposedException is deliberately NOT an invalid-socket signal. A socket only throws it
        // after its owner's Dispose, which sets the owner's disposed flag first — the callers'
        // when (_disposed) filters own that case. Seen live, it comes from a foreign object sharing the
        // try (a disposed session cryptor, a disposed downstream packet handler), and treating that as a
        // dead socket killed a server-wide transmitter for every session over one closing session's packet

        // Returns TRUE if the client is useless/dead
        return ex is SocketException {
            SocketErrorCode:
            SocketError.OperationAborted or // Socket closed during async op
            SocketError.Interrupted or      // Socket closed during blocking op
            SocketError.NotSocket or        // Handle is no longer a valid socket
            SocketError.ConnectionAborted   // Local network stack killed it
        };
    }
}