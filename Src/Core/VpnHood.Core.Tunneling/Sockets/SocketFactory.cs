using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Logging;

namespace VpnHood.Core.Tunneling.Sockets;

public class SocketFactory : ISocketFactory
{
    private bool _hasKeepAliveError;

    public virtual TcpClient CreateTcpClient(AddressFamily addressFamily)
    {
        return new TcpClient(addressFamily);
    }

    public virtual UdpClient CreateUdpClient(AddressFamily addressFamily)
    {
        return new UdpClient(0, addressFamily);
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