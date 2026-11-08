using System.Net;
using System.Net.Sockets;

namespace VpnHood.Net.Toolkit.Net;

/// <summary>
/// Provides a socket's endpoints as <see cref="IPEndPoint"/>s.
/// </summary>
public static class SocketExtensions
{
    /// <summary>Provides a socket's endpoints.</summary>
    /// <param name="socket">The socket.</param>
    extension(Socket socket)
    {
        /// <summary>Gets the socket's local endpoint.</summary>
        /// <returns>The endpoint.</returns>
        /// <exception cref="InvalidOperationException">The socket has no local endpoint.</exception>
        /// <exception cref="InvalidCastException">The endpoint is not an IP one.</exception>
        public IPEndPoint GetLocalEndPoint()
        {
            var endPoint = socket.LocalEndPoint;
            if (endPoint == null)
                throw new InvalidOperationException("Socket does not have a local endpoint.");

            return (IPEndPoint)endPoint;
        }

        /// <summary>Gets the socket's remote endpoint.</summary>
        /// <returns>The endpoint.</returns>
        /// <exception cref="InvalidOperationException">The socket has no remote endpoint.</exception>
        /// <exception cref="InvalidCastException">The endpoint is not an IP one.</exception>
        public IPEndPoint GetRemoteEndPoint()
        {
            var endPoint = socket.RemoteEndPoint;
            if (endPoint == null)
                throw new InvalidOperationException("Socket does not have a remote endpoint.");

            return (IPEndPoint)endPoint;
        }
    }
}