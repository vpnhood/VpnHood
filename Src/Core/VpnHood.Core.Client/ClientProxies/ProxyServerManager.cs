using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Tunneling.Sockets;

namespace VpnHood.Core.Client.ClientProxies;

public class ProxyServerManager(ProxyServerEndPoint[] optionsProxyServers, ISocketFactory socketFactory)
{
    public ProxyServerEndPoint[] OptionsProxyServers { get; } = optionsProxyServers;
    public ISocketFactory SocketFactory { get; } = socketFactory;

    public Task RemoveBadServers(CancellationToken linkedCtsToken)
    {
        throw new NotImplementedException();
    }

    public Task<TcpClient> ConnectAsync(IPEndPoint tcpEndPoint, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public int ProxyServerCount => throw new NotImplementedException();
}

/*
public class ProxyServerManager
{
    private readonly ISocketFactory _socketFactory;
    public ProxyServerStatus[] ProxyServerStatuses { get; private set; } = [];

    public ProxyServerManager(ProxyServerEndPoint[] proxyServerEndPoints,
        ISocketFactory socketFactory)
    {
        ProxyServers = proxyServerEndPoints.Select(x => new ProxyServer {
            EndPoint = x,
            Status = new ProxyServerStatus()
        }).ToArray();

        _socketFactory = socketFactory;
    }

    private ProxyServer[] _proxyServers = [];
    private ProxyServer[] ProxyServers {
        get => _proxyServers;
        set {
            _proxyServers = value;
            ProxyServerStatuses = value.Select(x => x.Status).ToArray();
        }
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

        // remove failed servers
        foreach (var (proxyServer, tcpClient, task) in testTasks) {
            tcpClient.Dispose();
            if (!task.IsCompletedSuccessfully) {
                proxyServer.SetSuccess(task.Result, false);
                proxyServer.Status.LastResponseDuration = task.Result;
                VhLogger.Instance.LogError(GeneralEventId.Essential, task.Exception,
                    "Failed to connect to the proxy server {ProxyServer}. Excluding it from the list.",
                    VhLogger.FormatHostName(proxyServer.EndPoint.Address));
            }
        }

        // calculate the average connection time and remove servers that took too long
        var connectionTimes = testTasks.Where(x => x.Item3.IsCompletedSuccessfully)
            .Select(x => x.Item3.Result).ToArray();
        var averageTime = connectionTimes.Any() ? connectionTimes.Average(ts => ts.TotalMilliseconds) : 0;
        var threshold = 2000 + averageTime * 2; // consider servers that take more than twice the average time as bad

        foreach (var (proxyServer, _, task) in testTasks) {
            if (task.IsCompletedSuccessfully) {
                if (task.Result.TotalMilliseconds > threshold) {
                    VhLogger.Instance.LogWarning(GeneralEventId.Essential,
                        "Proxy server took too long to connect. Mark it as inactive. {ProxyServer}",
                        VhLogger.FormatHostName(proxyServer.EndPoint.Address));
                    proxyServer.SetPoorInactive(task.Result);
                }
            }
            else {
                VhLogger.Instance.LogWarning(GeneralEventId.Essential,
                    "Proxy server took too long to connect. Mark it as unavailable. {ProxyServer}",
                    VhLogger.FormatHostName(proxyServer.EndPoint.Address));
                proxyServer.SetFailed();
            }
        }
    }

    public async Task<TcpClient> ConnectAsync(IPEndPoint ipEndPoint, CancellationToken cancellationToken)
    {
        // shuffle the proxy servers if in search mode
        ProxyServerInfo[] proxyServers;
        lock (this.proxyServers)
            proxyServers = this.proxyServers.OrderBy(_ => Random.Shared.Next()).ToArray();

        List<ProxyServerInfo> failedServers = [];

        // try to connect to a proxy server
        TcpClient? tcpClientOk = null;
        foreach (var proxyServer in proxyServers) {
            var tcpClient = _socketFactory.CreateTcpClient(ipEndPoint);
            try {
                VhLogger.Instance.LogDebug(GeneralEventId.Essential,
                    "Connecting via a proxy server {ProxyServer}...",
                    VhLogger.FormatHostName(proxyServer.EndPoint.Address));

                var socketOptions = await BuildSocksProxyOptions(proxyServer.EndPoint).Vhc();
                var client = new Socks5Client(socketOptions);
                await client.ConnectAsync(tcpClient, ipEndPoint, cancellationToken).Vhc();
                tcpClientOk = tcpClient;
                proxyServer.FailedCount = 0; // reset failed count on successful connection
                break;
            }
            catch (Exception ex) {
                tcpClient.Dispose();
                failedServers.Add(proxyServer);
                proxyServer.FailedCount++;
                proxyServer.TotalFailedCount++;
                VhLogger.Instance.LogError(GeneralEventId.Essential, ex,
                    "Failed to connect to the proxy server {ProxyServer}.",
                    VhLogger.FormatHostName(proxyServer.EndPoint.Address));
            }
        }

        // could not connect to any proxy server
        if (tcpClientOk is null)
            throw new SocketException((int)SocketError.NetworkUnreachable);

        // remove failed servers from the list
        lock (this.proxyServers)
            foreach (var failedServer in failedServers.Where(x => x.FailedCount > 1)) {
                this.proxyServers.Remove(failedServer);
                VhLogger.Instance.LogWarning(GeneralEventId.Essential,
                    "Proxy server has been removed from the list due to connection failures. {ProxyServer}",
                    VhLogger.FormatHostName(failedServer.EndPoint.Address));
            }

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

        public void SetFailed()
        {
            Status.FailedCount++;
            Status.TotalFailedCount++;
            Status.State = ProxyServerState.Unavailable;
        }

        public void SetActive(TimeSpan responseDuration)
        {
            Status.State = ProxyServerState.Active;
            Status.SucceededCount++;
            Status.LastResponseDuration = responseDuration;
            Status.LastConnectionTime = DateTime.Now;
        }

        public void SetPoorActive(TimeSpan responseDuration)
        {
            SetActive(responseDuration);
            Status.State = ProxyServerState.PoorActive;
        }

        public void SetPoorInactive(TimeSpan responseDuration)
        {
            SetActive(responseDuration);
            Status.State = ProxyServerState.PoorInactive;
        }
    }

}*/