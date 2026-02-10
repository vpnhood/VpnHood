using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.DomainFiltering;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Sockets;
using VpnHood.Core.Toolkit.Streams;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.Connections;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.Proxies;

namespace VpnHood.Core.Client;

internal class ClientStreamHandler(
    VpnHoodClient vpnHoodClient,
    ISocketFactory socketFactory,
    DomainFilteringService domainFilterService,
    Tunnel tunnel,
    ProxyManager proxyManager,
    TimeSpan tcpConnectTimeout,
    TransferBufferSize streamProxyBufferSize)
{
    private int _processingCount;
    private readonly ClientHostStat _stat = new();
    
    public IClientHostStat Stat => _stat;

    public async Task ProcessConnection(
        IConnection connection, IPEndPoint hostEndPoint, bool isInIpRange, CancellationToken cancellationToken)
    {
        try {
            // check cancellation
            Interlocked.Increment(ref _processingCount);
            cancellationToken.ThrowIfCancellationRequested();

            // create a scope for the logger
            VhLogger.Instance.LogDebug(GeneralEventId.ProxyChannel,
                "New TcpProxy Request. LocalPort: {LocalPort}, HostEp: {RemoteEp}",
                connection.RemoteEndPoint.Port, hostEndPoint);

            // Apply SNI filtering and update connection if needed
            (connection, isInIpRange) = await ApplySniFiltering(connection, hostEndPoint, isInIpRange, cancellationToken).Vhc();

            // Filter by IP
            if (isInIpRange) {
                // Create and add to tunnel channel
                await AddTunnelChannel(connection, hostEndPoint, cancellationToken).Vhc();
                _stat.TcpTunnelledCount++;
            }
            else {
                // Create and add to exclude channel
                await AddPassthruChannel(connection, hostEndPoint, cancellationToken).Vhc();
                _stat.TcpPassthruCount++;
            }
        }
        finally {
            Interlocked.Decrement(ref _processingCount);
        }
    }

    private async Task AddPassthruChannel(IConnection connection, IPEndPoint hostEndPoint, CancellationToken cancellationToken)
    {
        if (hostEndPoint.IsV6() && vpnHoodClient.SessionStatus?.IsIpV6SupportedByClient is null or false)
            throw new Exception("IPv6 is not supported by client.");

        //log
        VhLogger.Instance.LogDebug(GeneralEventId.ProxyChannel,
            "Adding a new stream channel to bypass. HostEp: {HostEp}, ConnectionId: {ConnectionId}",
            VhLogger.Format(hostEndPoint), connection.ConnectionId);

        // set timeout
        using var timeoutCts = new CancellationTokenSource(tcpConnectTimeout);
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

        // connect to host
        var tcpClient = socketFactory.CreateTcpClient(hostEndPoint);
        await tcpClient.ConnectAsync(hostEndPoint.Address, hostEndPoint.Port, connectCts.Token).Vhc();
        var hostConnection = new TcpConnection(tcpClient, connectionId: connection.ConnectionId, connectionName: "host", isServer: false);

        try {
            // create and add the channel
            var channel = new ProxyChannel(hostConnection.ToString(), connection, hostConnection, streamProxyBufferSize);
            proxyManager.AddChannel(channel, disposeOnFail: true);
        }
        catch {
            await hostConnection.DisposeAsync();
            throw;
        }
    }

    private async Task AddTunnelChannel(
        IConnection connection, IPEndPoint hostEndPoint, CancellationToken cancellationToken)
    {
        if (hostEndPoint.IsV6() && vpnHoodClient.SessionStatus?.IsIpV6SupportedByServer is null or false)
            throw new Exception("IPv6 is not supported by server.");

        //log
        VhLogger.Instance.LogDebug(GeneralEventId.ProxyChannel,
            "Adding a new stream channel to tunnel. HostEp: {HostEp}, ConnectionId: {ConnectionId}",
            VhLogger.Format(hostEndPoint), connection.ConnectionId);


        //handle small buffer for tiny TLS hello or small HTTP request to remove bidirectional pattern
        var memory = new Memory<byte>(new byte[TunnelDefaults.PrefetchStreamBufferSize]);
        var read = await connection.Stream.ReadAsync(memory, cancellationToken);
        var initContents = memory[..read];

        // Create the Request
        var request = new StreamProxyChannelRequest {
            RequestId = connection.ConnectionId,
            SessionId = vpnHoodClient.SessionId,
            SessionKey = vpnHoodClient.SessionKey,
            DestinationEndPoint = hostEndPoint
        };

        // read the response
        var requestEx = new ClientRequestEx { Request = request, PostBuffer = initContents };
        var requestResult = await vpnHoodClient.SendRequest<SessionResponse>(requestEx, cancellationToken).Vhc();
        try {
            var proxyConnection = requestResult.Connection;

            // add stream proxy
            var channel = new ProxyChannel(proxyConnection.ToString()!, connection, proxyConnection,
                streamProxyBufferSize);
            tunnel.AddChannel(channel, disposeIfFailed: true);
        }
        catch {
            requestResult.Dispose();
            throw;
        }
    }

    private async Task<(IConnection Connection, bool IsInIpRange)> ApplySniFiltering(
        IConnection connection, IPEndPoint hostEndPoint, bool isInIpRange, CancellationToken cancellationToken)
    {
        // Filter by SNI
        var filterResult = await domainFilterService.ProcessStream(
            connection.Stream, hostEndPoint, cancellationToken).Vhc();

        switch (filterResult.Action) {
            case DomainFilterAction.Block:
                VhLogger.Instance.LogInformation(GeneralEventId.Sni,
                    "Domain has been blocked. Domain: {Domain}",
                    VhLogger.FormatHostName(filterResult.DomainName));

                throw new Exception($"Domain has been blocked. Domain: {filterResult.DomainName}");

            case DomainFilterAction.Exclude:
                VhLogger.Instance.LogDebug(GeneralEventId.Sni,
                    "Domain has been excluded from VPN. Domain: {Domain}",
                    VhLogger.FormatHostName(filterResult.DomainName));
                isInIpRange = false;
                break;

            case DomainFilterAction.Include:
                VhLogger.Instance.LogDebug(GeneralEventId.Sni,
                    "Domain has been included in VPN. Domain: {Domain}",
                    VhLogger.FormatHostName(filterResult.DomainName));
                isInIpRange = true;
                break;

            case DomainFilterAction.None:
            default:
                // default isInIpRange
                break;
        }

        // update connection stream with ReadBufferedStream to pre-append the read data
        if (filterResult.ReadData.Length > 0)
            connection = new ConnectionDecorator(connection,
                new ReadBufferedStream(connection.Stream, leaveOpen: false, filterResult.ReadData.Span) {
                    AllowBufferRefill = false
                });

        return (connection, isInIpRange);
    }

    private class ClientHostStat : IClientHostStat
    {
        public int TcpTunnelledCount { get; set; }
        public int TcpPassthruCount { get; set; }
    }
}