using System;
using System.Net.Sockets;
using VpnHood.Tunneling.Factory;

namespace VpnHood.Server.App;

class ServerSocketFactory : SocketFactory
{
    public override void SetKeepAlive(Socket socket, bool enable, TimeSpan? TcpKeepAliveTime, TimeSpan? TcpKeepAliveInterval, int? TcpKeepAliveRetryCount)
    {
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, enable);

        // cast to (int) required for linux
        if (TcpKeepAliveTime!=null ) socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, (int)TcpKeepAliveTime.Value.TotalSeconds);
        if (TcpKeepAliveInterval!=null ) socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, (int)TcpKeepAliveInterval.Value.TotalSeconds);
        if (TcpKeepAliveRetryCount != null) socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, (int)TcpKeepAliveRetryCount);
    }
}