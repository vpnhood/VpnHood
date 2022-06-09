using System;
using System.Net.Sockets;
using VpnHood.Tunneling.Factory;

namespace VpnHood.Server.App;

class ServerSocketFactory : SocketFactory
{
    public override void SetKeepAlive(Socket socket, bool enable, TimeSpan? tcpKeepAliveTime = null, TimeSpan? tcpKeepAliveInterval = null, int? tcpKeepAliveRetryCount = null)
    {
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, enable);

        // cast to (int) required for linux
        if (tcpKeepAliveTime!=null ) socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, (int)tcpKeepAliveTime.Value.TotalSeconds);
        if (tcpKeepAliveInterval!=null ) socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, (int)tcpKeepAliveInterval.Value.TotalSeconds);
        if (tcpKeepAliveRetryCount != null) socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, (int)tcpKeepAliveRetryCount);
    }
}