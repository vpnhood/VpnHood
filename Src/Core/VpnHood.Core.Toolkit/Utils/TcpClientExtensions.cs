using System.Net;
using System.Net.Sockets;

namespace VpnHood.Core.Toolkit.Utils;

public static class TcpClientExtensions
{
    public static async Task VhConnectAsync(this TcpClient tcpClient, IPEndPoint ipEndPoint,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        await tcpClient.ConnectAsync(ipEndPoint.Address, ipEndPoint.Port, linkedCts.Token);
    }
}