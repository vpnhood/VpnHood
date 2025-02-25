using System.Net;
using System.Net.Sockets;

namespace VpnHood.Core.Toolkit.Utils;

public static class TcpClientExtensions
{
    // compatible with .NET 2.1
    public static Task VhConnectAsync(this TcpClient tcpClient, IPEndPoint ipEndPoint,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        return VhUtils.RunTask(tcpClient.ConnectAsync(ipEndPoint.Address, ipEndPoint.Port), timeout, cancellationToken);
    }

    // compatible with .NET 2.1
    public static Task VhConnectAsync(this TcpClient tcpClient, IPEndPoint ipEndPoint,
        CancellationToken cancellationToken)
    {
        return VhUtils.RunTask(tcpClient.ConnectAsync(ipEndPoint.Address, ipEndPoint.Port), cancellationToken);
    }
}