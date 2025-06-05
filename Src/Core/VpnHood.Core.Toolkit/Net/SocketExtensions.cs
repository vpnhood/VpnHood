using System.Net;
using System.Net.Sockets;

namespace VpnHood.Core.Toolkit.Net;

public static class SocketExtensions
{
    public static  IPEndPoint GetLocalEndPoint(this Socket socket)
    {
        var endPoint = socket.LocalEndPoint;
        if (endPoint == null)
            throw new InvalidOperationException("Socket does not have a local endpoint.");

        return (IPEndPoint)endPoint;
    }

    public static IPEndPoint GetRemoteEndPoint(this Socket socket)
    {
        var endPoint = socket.RemoteEndPoint;
        if (endPoint == null)
            throw new InvalidOperationException("Socket does not have a remote endpoint.");

        return (IPEndPoint)endPoint;
    }
}