using System;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Logging;
using VpnHood.Common.Net;

namespace VpnHood.Tunneling.Factory;

public class SocketFactory : ISocketFactory
{
    private bool _isKeepAliveHasError = false;

    public virtual TcpClient CreateTcpClient(AddressFamily addressFamily)
    {
        var anyIpAddress = IPAddressUtil.GetAnyIpAddress(addressFamily);
        return new TcpClient(new IPEndPoint(anyIpAddress, 0));
    }

    public virtual UdpClient CreateUdpClient(AddressFamily addressFamily)
    {
        return new UdpClient(0, addressFamily);
    }

    public virtual void SetKeepAlive(Socket socket, bool enable)
    {
        if (_isKeepAliveHasError)
            return;

        try
        {
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, enable);

            // .NET Standard 2.1 compatibility
            // cast to (int) required for linux
            // if (tcpKeepAliveTime != null) socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, (int)tcpKeepAliveTime.Value.TotalSeconds);
            // if (tcpKeepAliveInterval != null) socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, (int)tcpKeepAliveInterval.Value.TotalSeconds);
            // if (tcpKeepAliveRetryCount != null) socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, (int)tcpKeepAliveRetryCount);
        }
        catch (Exception ex)
        {
            _isKeepAliveHasError = true;
            VhLogger.Instance.LogWarning(ex, "KeepAlive is not supported! Consider upgrading your OS.");
        }
    }
}