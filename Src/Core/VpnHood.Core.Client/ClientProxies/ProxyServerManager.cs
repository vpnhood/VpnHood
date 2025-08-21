using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.SocksProxy.Socks5Proxy;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Sockets;

namespace VpnHood.Core.Client.ClientProxies;

public class ProxyServerManager
{
    private readonly ISocketFactory _socketFactory;
    public ProxyServerStatus[] ProxyServerStatuses { get; private set; } = [];
    private ProxyServer[] ProxyServers { get; }
    public bool IsEnabled => ProxyServers.Any();

    public ProxyServerManager(ProxyServerEndPoint[] proxyServerEndPoints,
        ISocketFactory socketFactory)
    {
        ProxyServers = proxyServerEndPoints.Select(x => new ProxyServer {
            EndPoint = x,
            Status = new ProxyServerStatus()
        }).ToArray();

        _socketFactory = socketFactory;
    }

    public async Task RemoveBadServers(CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug(GeneralEventId.Essential, "Checking proxy servers for reachability...");

        // connect to each proxy server and check if it is reachable
        List<(ProxyServer, TcpClient, Task<TimeSpan>)> testTasks = [];
        foreach (var proxyServer in ProxyServers) {
            var client = new Socks5Client(await BuildSocksProxyOptions(proxyServer.EndPoint));
            var tcpClient = _socketFactory.CreateTcpClient(client.ProxyEndPoint);
            var checkTask = client.CheckConnectionAsync(tcpClient, cancellationToken);
            testTasks.Add((proxyServer, tcpClient, checkTask));
        }

        // wait for all tasks to complete even if some fail
        while (testTasks.Any(x => !x.Item3.IsCompleted))
            await Task.WhenAny(testTasks.Select(x => x.Item3)).Vhc();

        // calculate the average connection time and remove servers that took too long
        var connectionTimes = testTasks.Where(x => x.Item3.IsCompletedSuccessfully)
            .Select(x => x.Item3.Result).ToArray();
        var averageTime = connectionTimes.Any() ? connectionTimes.Average(ts => ts.TotalMilliseconds) : 0;
        var threshold = 2000 + averageTime * 2; // consider servers that take more than twice the average time as bad

        foreach (var (proxyServer, tcpClient, task) in testTasks) {
            tcpClient.Dispose();

            // set failed
            if (!task.IsCompletedSuccessfully) {
                proxyServer.SetFailed(inactive:true);
                continue;
            }

            // set active or poor active
            if (task.Result.TotalMilliseconds <= threshold)
                proxyServer.SetActive(task.Result);
            else
                proxyServer.SetPoor(task.Result, inactive: true); // inactive poor on bad servers
        }
    }

    public async Task<TcpClient> ConnectAsync(IPEndPoint ipEndPoint, CancellationToken cancellationToken)
    {
        // order by active state then Last response duration
        var proxyServers = ProxyServers.Where(x=>x.ShouldUse).OrderBy(_ => Random.Shared.Next()).ToArray();

        // try to connect to a proxy server
        TcpClient? tcpClientOk = null;
        foreach (var proxyServer in proxyServers) {
            var tcpClient = _socketFactory.CreateTcpClient(ipEndPoint);
            var tickCount = Environment.TickCount64;
            try {
                VhLogger.Instance.LogDebug(GeneralEventId.Essential,
                    "Connecting via a proxy server {ProxyServer}...",
                    VhLogger.FormatHostName(proxyServer.EndPoint.Address));

                var socketOptions = await BuildSocksProxyOptions(proxyServer.EndPoint).Vhc();
                var client = new Socks5Client(socketOptions);
                await client.ConnectAsync(tcpClient, ipEndPoint, cancellationToken).Vhc();
                tcpClientOk = tcpClient;
                proxyServer.SetActive(TimeSpan.FromMilliseconds(Environment.TickCount64 - tickCount));
                break;
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(GeneralEventId.Essential, ex,
                    "Failed to connect to proxy server {ProxyServer}.",
                    VhLogger.FormatHostName(proxyServer.EndPoint.Address));

                proxyServer.SetFailed(inactive: false);
                tcpClient.Dispose();
                proxyServer.Status.FailedCount++;
                proxyServer.Status.TotalFailedCount++;
            }
        }

        // could not connect to any proxy server
        if (tcpClientOk is null)
            throw new SocketException((int)SocketError.NetworkUnreachable);

        return tcpClientOk;
    }

    private static async Task<Socks5Options> BuildSocksProxyOptions(ProxyServerEndPoint proxyServerEndPoint)
    {
        ArgumentException.ThrowIfNullOrEmpty(proxyServerEndPoint.Address);

        // try parse the proxy address
        if (!IPAddress.TryParse(proxyServerEndPoint.Address, out var address)) {
            var entry = await Dns.GetHostEntryAsync(proxyServerEndPoint.Address).Vhc();
            if (entry.AddressList.Length == 0)
                throw new Exception("Failed to resolve proxy server address.");
            // select a random address if multiple addresses are returned
            address = entry.AddressList[Random.Shared.Next(entry.AddressList.Length)];
        }

        return new Socks5Options {
            ProxyEndPoint = new IPEndPoint(address, proxyServerEndPoint.Port),
            Username = proxyServerEndPoint.Username,
            Password = proxyServerEndPoint.Password,
        };
    }

    private class ProxyServer
    {
        public required ProxyServerEndPoint EndPoint { get; init; }
        public ProxyServerStatus Status = new();

        public bool ShouldUse => Status.State is
            ProxyServerState.Unknown or
            ProxyServerState.Active or
            ProxyServerState.PoorActive;

        public void SetActive(TimeSpan responseDuration)
        {
            Status.State = ProxyServerState.Active;
            Status.SucceededCount++;
            Status.LastResponseDuration = responseDuration;
            Status.LastConnectionTime = DateTime.Now;
        }

        public void SetPoor(TimeSpan responseDuration, bool inactive)
        {
            SetActive(responseDuration);
            Status.State = inactive ? ProxyServerState.PoorInactive : ProxyServerState.PoorActive;
            VhLogger.Instance.LogWarning(GeneralEventId.Essential,
                "Set proxy server state. {ProxyServer}, State: {State}",
                VhLogger.FormatHostName(EndPoint.Address), Status.State);
        }
        public void SetFailed(bool inactive)
        {
            Status.FailedCount++;
            Status.TotalFailedCount++;
            Status.State = inactive ? ProxyServerState.FailedInactive : ProxyServerState.Failed;
            VhLogger.Instance.LogWarning(GeneralEventId.Essential,
                "Set proxy server state. {ProxyServer}, State: {State}",
                VhLogger.FormatHostName(EndPoint.Address), Status.State);
        }

    }

}