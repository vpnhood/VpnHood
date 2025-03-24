using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Tunneling.Sockets;

public class SocketFactory : ISocketFactory
{
    private bool _hasKeepAliveError;

    public virtual TcpClient CreateTcpClient(IPEndPoint ipEndPoint)
    {
        var localEndPoint = new IPEndPoint(ipEndPoint.IsV4() ? IPAddress.Any : IPAddress.IPv6Any, 0);
        var tcpClient = new TcpClient(ipEndPoint.AddressFamily);
        tcpClient.Client.Bind(localEndPoint);
        return tcpClient;
    }

    public virtual UdpClient CreateUdpClient(AddressFamily addressFamily)
    {
        var localEndPoint = new IPEndPoint(addressFamily.IsV4() ? IPAddress.Any : IPAddress.IPv6Any, 0);
        var udpClient = new UdpClient(addressFamily);
        udpClient.Client.Bind(localEndPoint);
        return udpClient;
    }

    public virtual void SetKeepAlive(Socket socket, bool enable)
    {
        if (_hasKeepAliveError)
            return;

        try {
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, enable);

            // .NET Standard 2.1 compatibility
            // cast to (int) required for linux
            // if (tcpKeepAliveTime != null) socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, (int)tcpKeepAliveTime.Value.TotalSeconds);
            // if (tcpKeepAliveInterval != null) socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, (int)tcpKeepAliveInterval.Value.TotalSeconds);
            // if (tcpKeepAliveRetryCount != null) socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, (int)tcpKeepAliveRetryCount);
        }
        catch (Exception ex) {
            _hasKeepAliveError = true;
            VhLogger.Instance.LogWarning(ex, "KeepAlive is not supported! Consider upgrading your OS.");
        }
    }
}