using Microsoft.Extensions.Logging;
using System.Net;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Filtering.DomainFiltering;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Sockets;
using VpnHood.Core.Toolkit.Streams;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.Connections;
using VpnHood.Core.Tunneling.Exceptions;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.Proxies;

namespace VpnHood.Core.Client;

internal class ClientStreamHandler(
    ClientSession session,
    ulong sessionId,
    ReadOnlyMemory<byte> sessionKey,
    ISocketFactory socketFactory,
    DomainFilteringService domainFilterService,
    Tunnel tunnel,
    ProxyManager proxyManager,
    TimeSpan tcpConnectTimeout,
    NetFilter netFilter,
    TransferBufferSize streamProxyBufferSize)
{
    private int _processingCount;
    private readonly ClientHostStat _stat = new();

    public IClientHostStat Stat => _stat;

    public async Task ProcessConnection(IConnection connection, IPEndPoint hostEndPoint, 
        CancellationToken cancellationToken)
    {
        try {
            // check cancellation
            Interlocked.Increment(ref _processingCount);
            cancellationToken.ThrowIfCancellationRequested();

            // create a scope for the logger
            VhLogger.Instance.LogDebug(GeneralEventId.ProxyChannel,
                "New TcpProxy Request. LocalPort: {LocalPort}, HostEp: {RemoteEp}",
                connection.RemoteEndPoint.Port, hostEndPoint);

            var filterAction = FilterAction.Default;

            // Apply SNI filtering and update connection if needed
            if (domainFilterService.IsEnabled)
                (connection, filterAction) = await ApplySniFiltering(connection, hostEndPoint, cancellationToken).Vhc();

            // Filter by IP if SNI filtering result is default
            if (filterAction is FilterAction.Default && netFilter.IpFilter != null)
                filterAction = netFilter.IpFilter.Process(IpProtocol.Tcp, hostEndPoint.ToValue());

            switch (filterAction)
            {
                case FilterAction.Block:
                    throw new NetFilterException("A host has been blocked.");

                case FilterAction.Include:
                    // Create and add to tunnel channel
                    VhLogger.Instance.LogDebug(GeneralEventId.ProxyChannel, "Include a Host to VPN. HostEp: {HostEp}", VhLogger.Format(hostEndPoint));
                    await AddTunnelChannel(connection, hostEndPoint, cancellationToken).Vhc();
                    _stat.TcpTunnelledCount++;
                    break;

                default: // exclude
                    // Create and add to exclude channel
                    VhLogger.Instance.LogDebug(GeneralEventId.ProxyChannel, "Exclude a Host from VPN. HostEp: {HostEp}", VhLogger.Format(hostEndPoint));
                    await AddPassthruChannel(connection, hostEndPoint, cancellationToken).Vhc();
                    _stat.TcpPassthruCount++;
                    break;
            }
        }
        finally {
            Interlocked.Decrement(ref _processingCount);
        }
    }

    private async Task AddPassthruChannel(IConnection connection, IPEndPoint hostEndPoint, CancellationToken cancellationToken)
    {
        // apply NetMapper
        if (netFilter.IpMapper?.ToHost(IpProtocol.Tcp, hostEndPoint.ToValue(), out var newEndPoint) == true)
            hostEndPoint = newEndPoint.ToIPEndPoint();

        // check IPv6 support
        if (hostEndPoint.IsV6() && !session.Status.IsIpV6SupportedByClient)
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
        if (hostEndPoint.IsV6() && !session.Status.IsIpV6SupportedByServer)
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
            SessionId = sessionId,
            SessionKey = sessionKey,
            DestinationEndPoint = hostEndPoint
        };

        // read the response
        var requestEx = new ClientRequestEx { Request = request, PostBuffer = initContents };
        var requestResult = await session.SendRequest<SessionResponse>(requestEx, cancellationToken).Vhc();
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

    private async Task<(IConnection Connection, FilterAction filterAction)> ApplySniFiltering(
        IConnection connection, IPEndPoint hostEndPoint, CancellationToken cancellationToken)
    {
        // Filter by SNI (it log by its own observer, no need to log here)
        var filterResult = await domainFilterService.ProcessStream(
            connection.Stream, hostEndPoint, cancellationToken).Vhc();

        // update connection stream with ReadBufferedStream to pre-append the read data
        if (filterResult.ReadData.Length > 0)
            connection = new ConnectionDecorator(connection,
                new ReadBufferedStream(connection.Stream, leaveOpen: false, filterResult.ReadData.Span) {
                    AllowBufferRefill = false
                });

        return (connection, filterResult.Action);
    }

    private class ClientHostStat : IClientHostStat
    {
        public int TcpTunnelledCount { get; set; }
        public int TcpPassthruCount { get; set; }
    }
}