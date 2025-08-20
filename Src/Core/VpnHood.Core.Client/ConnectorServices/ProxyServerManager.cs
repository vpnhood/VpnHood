using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Core.SocksProxy.Socks5Proxy;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Sockets;

namespace VpnHood.Core.Client.ConnectorServices;

public class ProxyServerManager(
    ProxyServerOptions[] proxyServers,
    ISocketFactory socketFactory)
{
    private readonly List<ProxyServer> _proxyServers = proxyServers.Select(x => new ProxyServer(x)).ToList();
    public int ProxyServerCount {
        get {
            lock (_proxyServers)
                return _proxyServers.Count;
        }
    }
    public async Task RemoveBadServers(CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug(GeneralEventId.Essential, "Checking proxy servers for reachability...");

        ProxyServer[] proxyServers;
        lock (_proxyServers)
            proxyServers = _proxyServers.ToArray();

        // connect to each proxy server and check if it is reachable
        List<(ProxyServer, TcpClient, Task<TimeSpan>)> testTasks = [];
        foreach (var proxyServer in proxyServers) {
            var client = new Socks5Client(await BuildSocksProxyOptions(proxyServer.Options));
            var tcpClient = socketFactory.CreateTcpClient(client.ProxyEndPoint);
            var checkTask = client.CheckConnectionAsync(tcpClient, cancellationToken);
            testTasks.Add((proxyServer, tcpClient, checkTask));
        }

        // wait for all tasks to complete even if some fail
        while(testTasks.Any(x=>!x.Item3.IsCompleted))
            await Task.WhenAny(testTasks.Select(x => x.Item3)).Vhc();

        // remove failed servers
        foreach (var (proxyServer, tcpClient, task) in testTasks) {
            tcpClient.Dispose();
            if (!task.IsCompletedSuccessfully) {
                lock (_proxyServers)
                    _proxyServers.Remove(proxyServer);

                VhLogger.Instance.LogError(GeneralEventId.Essential, task.Exception,
                    "Failed to connect to the proxy server {ProxyServer}. Excluding it from the list.",
                    VhLogger.FormatHostName(proxyServer.Options.Address));
            }
        }

        // each task return a TimeSpan with the connection time
        // remove servers that took too much longer than others
        var connectionTimes = testTasks.Where(x => x.Item3.IsCompletedSuccessfully)
            .Select(x => x.Item3.Result).ToList();

        // throw if no proxy servers are reachable
        if (connectionTimes.Count == 0)
            throw new UnreachableProxyServerException("All proxy servers are unreachable.");

        // calculate the average connection time and remove servers that took too long
        var averageTime = connectionTimes.Average(ts => ts.TotalMilliseconds);
        var threshold = 2000 + averageTime * 2; // consider servers that take more than twice the average time as bad
        foreach (var (proxyServer, _, task) in testTasks) {
            if (task.Result.TotalMilliseconds > threshold) {
                lock (_proxyServers)
                    _proxyServers.Remove(proxyServer);

                VhLogger.Instance.LogWarning(GeneralEventId.Essential,
                    "Proxy server took too long to connect. Excluding it from the list. {ProxyServer}",
                    VhLogger.FormatHostName(proxyServer.Options.Address));
            }
        }
    }

    public async Task<TcpClient> ConnectAsync(IPEndPoint ipEndPoint, CancellationToken cancellationToken)
    {
        // shuffle the proxy servers if in search mode
        ProxyServer[] proxyServers;
        lock (_proxyServers)
            proxyServers = _proxyServers.OrderBy(_ => Random.Shared.Next()).ToArray();

        List<ProxyServer> failedServers = [];

        // try to connect to a proxy server
        TcpClient? tcpClientOk = null;
        foreach (var proxyServer in proxyServers) {
            var tcpClient = socketFactory.CreateTcpClient(ipEndPoint);
            try {
                VhLogger.Instance.LogDebug(GeneralEventId.Essential,
                    "Connecting via a proxy server {ProxyServer}...", 
                    VhLogger.FormatHostName(proxyServer.Options.Address));

                var socketOptions = await BuildSocksProxyOptions(proxyServer.Options).Vhc();
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
                    VhLogger.FormatHostName(proxyServer.Options.Address));
            }
        }

        // could not connect to any proxy server
        if (tcpClientOk is null)
            throw new SocketException((int)SocketError.NetworkUnreachable);

        // remove failed servers from the list
        lock (_proxyServers) {
            foreach (var failedServer in failedServers.Where(x => x.FailedCount > 1)) {
                _proxyServers.Remove(failedServer);
                VhLogger.Instance.LogWarning(GeneralEventId.Essential,
                    "Proxy server has been removed from the list due to connection failures. {ProxyServer}",
                    VhLogger.FormatHostName(failedServer.Options.Address));
            }
        }

        return tcpClientOk;
    }

    private static async Task<Socks5Options> BuildSocksProxyOptions(ProxyServerOptions proxyServerOptions)
    {
        ArgumentException.ThrowIfNullOrEmpty(proxyServerOptions.Address);

        // try parse the proxy address
        if (!IPAddress.TryParse(proxyServerOptions.Address, out var address)) {
            var entry = await Dns.GetHostEntryAsync(proxyServerOptions.Address).Vhc();
            if (entry.AddressList.Length == 0)
                throw new Exception("Failed to resolve proxy server address.");
            // select a random address if multiple addresses are returned
            address = entry.AddressList[Random.Shared.Next(entry.AddressList.Length)];
        }

        return new Socks5Options {
            ProxyEndPoint = new IPEndPoint(address, proxyServerOptions.Port),
            Username = proxyServerOptions.Username,
            Password = proxyServerOptions.Password,
        };
    }

    private class ProxyServer(ProxyServerOptions options)
    {
        public ProxyServerOptions Options { get; } = options;
        public int FailedCount { get; set; }
        public int TotalFailedCount { get; set; }
    }
}