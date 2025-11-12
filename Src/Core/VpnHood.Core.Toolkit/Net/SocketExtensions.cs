using System.Net;
using System.Net.Sockets;

namespace VpnHood.Core.Toolkit.Net;

public static class SocketExtensions
{
    extension(Socket socket)
    {
        public IPEndPoint GetLocalEndPoint()
        {
            var endPoint = socket.LocalEndPoint;
            if (endPoint == null)
                throw new InvalidOperationException("Socket does not have a local endpoint.");

            return (IPEndPoint)endPoint;
        }

        public IPEndPoint GetRemoteEndPoint()
        {
            var endPoint = socket.RemoteEndPoint;
            if (endPoint == null)
                throw new InvalidOperationException("Socket does not have a remote endpoint.");

            return (IPEndPoint)endPoint;
        }
    }
}