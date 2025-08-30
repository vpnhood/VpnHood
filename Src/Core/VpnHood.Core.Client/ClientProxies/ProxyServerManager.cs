using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Proxies;
using VpnHood.Core.Proxies.Socks5ProxyClients;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Sockets;

namespace VpnHood.Core.Client.ClientProxies;

public class ProxyServerManager
{
    private readonly ISocketFactory _socketFactory;
    public ProxyServerStatus[] ProxyServerStatuses => ProxyServers.Select(x => x.Status).ToArray();
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
            if (proxyServer.EndPoint.ProxyServerType != ProxyServerType.Socks5)
            {
                VhLogger.Instance.LogWarning("Skipping unsupported proxy type {ProxyType} for server {ProxyServer}",
                    proxyServer.EndPoint.ProxyServerType, VhLogger.FormatHostName(proxyServer.EndPoint.Address));
                proxyServer.Status.IsActive = false;
                continue;
            }

            var client = new Socks5ProxyClient(await BuildSocksProxyOptions(proxyServer.EndPoint));
            var tcpClient = _socketFactory.CreateTcpClient(client.ProxyEndPoint);
            var checkTask = client.CheckConnectionAsync(tcpClient, cancellationToken);
            testTasks.Add((proxyServer, tcpClient, checkTask));
        }

        // wait for all tasks to complete even if some fail
        while (testTasks.Any(x => !x.Item3.IsCompleted))
            await Task.WhenAny(testTasks.Select(x => x.Item3)).Vhc();

        // find the fastest server and set threshold
        var succeededTasks = testTasks
            .Where(x => x.Item3.IsCompletedSuccessfully && x.Item1.Status.IsActive)
            .OrderBy(x => x.Item3.Result)
            .ToArray();

        // set fastest server
        TimeSpan? fastestLatency = succeededTasks.Any() ? succeededTasks.First().Item3.Result: null;

        // update server statuses
        foreach (var (proxyServer, tcpClient, task) in testTasks) {
            tcpClient.Dispose();

            // set failed
            if (!task.IsCompletedSuccessfully) {
                proxyServer.RecordFailed(_requestCount);
                proxyServer.Status.IsActive = false;
                continue;
            }

            // set active or poor active
            proxyServer.RecordSuccess(latency: task.Result, fastestLatency: fastestLatency, _requestCount);
        }
    }

    private ProxyServer? _fastestServer;
    private int _requestCount;
    public async Task<TcpClient> ConnectAsync(IPEndPoint ipEndPoint, CancellationToken cancellationToken)
    {
        _requestCount++;

        // order by active state then Last response duration
        var proxyServers = ProxyServers
            .Where(x => x.Status.IsActive)
            .OrderBy(x => x.GetSortValue(_requestCount))
            .ToArray();

        // try to connect to a proxy server
        TcpClient? tcpClientOk = null;
        foreach (var proxyServer in proxyServers) {
            var tcpClient = _socketFactory.CreateTcpClient(ipEndPoint);
            var tickCount = Environment.TickCount64;
            try {
                VhLogger.Instance.LogDebug(GeneralEventId.Essential,
                    "Connecting via a {ProxyType} proxy server {ProxyServer}...",
                    proxyServer.EndPoint.ProxyServerType, VhLogger.FormatHostName(proxyServer.EndPoint.Address));

                await ConnectToProxyAsync(proxyServer.EndPoint, tcpClient, ipEndPoint, cancellationToken).Vhc();
                tcpClientOk = tcpClient;

                // find the fastest server
                var latency = TimeSpan.FromMilliseconds(Environment.TickCount64 - tickCount);
                if (!proxyServers.Contains(_fastestServer) || 
                    latency < _fastestServer?.Status.Latency )
                    _fastestServer = proxyServer;

                proxyServer.RecordSuccess(latency, fastestLatency: _fastestServer?.Status.Latency, _requestCount);
                break;
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(GeneralEventId.Essential, ex,
                    "Failed to connect to {ProxyType} proxy server {ProxyServer}.",
                    proxyServer.EndPoint.ProxyServerType, VhLogger.FormatHostName(proxyServer.EndPoint.Address));

                proxyServer.RecordFailed(_requestCount);
                tcpClient.Dispose();
            }
        }

        // could not connect to any proxy server
        if (tcpClientOk is null)
            throw new SocketException((int)SocketError.NetworkUnreachable);

        return tcpClientOk;
    }

    //private IProxyClient CreateProxyClient()

    private async Task ConnectToProxyAsync(ProxyServerEndPoint proxyServerEndPoint, TcpClient tcpClient, IPEndPoint destination, CancellationToken cancellationToken)
    {
        switch (proxyServerEndPoint.ProxyServerType)
        {
            case ProxyServerType.Socks5:
                var socks5Options = await BuildSocksProxyOptions(proxyServerEndPoint);
                var socks5Client = new Socks5ProxyClient(socks5Options);
                await socks5Client.ConnectAsync(tcpClient, destination, cancellationToken);
                break;
                
            case ProxyServerType.Socks4:
                throw new NotSupportedException("SOCKS4 proxy support requires additional implementation");
                
            case ProxyServerType.Http:
                throw new NotSupportedException("HTTP proxy support requires additional implementation");
                
            case ProxyServerType.Https:
                throw new NotSupportedException("HTTPS proxy support requires additional implementation");
                
            default:
                throw new NotSupportedException($"Proxy type {proxyServerEndPoint.ProxyServerType} is not supported");
        }
    }

    private static async Task<Socks5ProxyClientOptions> BuildSocksProxyOptions(ProxyServerEndPoint proxyServerEndPoint)
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

        return new Socks5ProxyClientOptions() {
            ProxyEndPoint = new IPEndPoint(address, proxyServerEndPoint.Port),
            Username = proxyServerEndPoint.Username,
            Password = proxyServerEndPoint.Password,
        };
    }
}

public class ProxyClientFactory
{
    public static IPAddress GetIpAddress()
    {
        throw new NotImplementedException();
    }

    public static IProxyClient CreateProxyClient(ProxyServerEndPoint proxyServerEndPoint)
    {
        switch (proxyServerEndPoint.ProxyServerType) {
            case ProxyServerType.Socks5:
                throw new NotImplementedException();
            //return new Socks5ProxyClient("sfsfsf");

            case ProxyServerType.Socks4:
                throw new NotSupportedException("SOCKS4 proxy support requires additional implementation");

            case ProxyServerType.Http:
                throw new NotSupportedException("HTTP proxy support requires additional implementation");

            case ProxyServerType.Https:
                throw new NotSupportedException("HTTPS proxy support requires additional implementation");

            default:
                throw new NotSupportedException($"Proxy type {proxyServerEndPoint.ProxyServerType} is not supported");
        }
    }
}