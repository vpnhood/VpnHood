using Microsoft.Extensions.Logging;
using System.Net;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Ads;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Filtering.DomainFiltering;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Sockets;
using VpnHood.Net.Toolkit.Streams;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.Connections;
using VpnHood.Core.Tunneling.Exceptions;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.Forwarding;
using VpnHood.Core.Common.Configuration;

namespace VpnHood.Core.Client;

internal class ClientStreamHandler(
    ClientSession session,
    ulong sessionId,
    ReadOnlyMemory<byte> sessionKey,
    ISocketFactory socketFactory,
    DomainFilteringService domainFilterService,
    Tunnel tunnel,
    TrafficMeter trafficMeter,
    ForwardingManager forwardingManager,
    TimeSpan tcpConnectTimeout,
    NetFilter netFilter,
    TransferBufferSize streamForwardingBufferSize,
    IReadOnlyList<IPAddress> dnsServers,
    SplitDnsMode splitDnsMode,
    PassthroughState passthroughState)
{
    private int _processingCount;
    private readonly ClientHostStat _stat = new();
    private readonly IpAddressValue[] _dnsServers = dnsServers.Select(x => x.ToValue()).ToArray();

    public IClientHostStat Stat => _stat;

    public async Task ProcessConnection(IStreamConnection streamConnection, IPEndPoint hostEndPoint,
        CancellationToken cancellationToken)
    {
        try {
            // check cancellation
            Interlocked.Increment(ref _processingCount);
            cancellationToken.ThrowIfCancellationRequested();

            // the rules this connection is decided by: a change of them before it goes out stops it
            var filterGeneration = session.FilterGeneration;

            // create a scope for the logger
            VhLogger.Instance.LogDebug(GeneralEventId.StreamForwardingChannel,
                "New TCP connection. LocalPort: {LocalPort}, HostEp: {RemoteEp}",
                streamConnection.RemoteEndPoint.Port, hostEndPoint);

            var filterAction = FilterAction.Default;

            // Apply SNI filtering and update connection if needed
            if (domainFilterService.IsEnabled)
                (streamConnection, filterAction) = await ApplySniFiltering(streamConnection, hostEndPoint, cancellationToken).Vhc();

            // Filter by IP if SNI filtering result is default
            var hostEndPointValue = hostEndPoint.ToValue();
            if (filterAction is FilterAction.Default && netFilter.IpFilter != null)
                filterAction = netFilter.IpFilter.Process(IpProtocol.Tcp, hostEndPointValue);

            // an ad's passthrough, and DNS kept inside, as the packet path has them: this path decides again whatever
            // the packet path chose for the connection's packets
            filterAction = ClientFilterOverrides.Apply(filterAction, hostEndPointValue,
                passthroughState.PassthroughForAd, _dnsServers, splitDnsMode);

            switch (filterAction) {
                case FilterAction.Block:
                    throw new NetFilterException("A host has been blocked.");

                case FilterAction.Exclude:
                    // Create and add to exclude channel
                    VhLogger.Instance.LogDebug(GeneralEventId.StreamForwardingChannel, "Exclude a Host from VPN. HostEp: {HostEp}", VhLogger.Format(hostEndPoint));
                    await AddPassthruChannel(streamConnection, hostEndPoint, filterGeneration, cancellationToken).Vhc();
                    _stat.TcpPassthruCount++;
                    break;

                default: // include or default — tunnel unless a gate vetoed (fail-closed)
                    // Create and add to tunnel channel
                    VhLogger.Instance.LogDebug(GeneralEventId.StreamForwardingChannel, "Include a Host to VPN. HostEp: {HostEp}", VhLogger.Format(hostEndPoint));
                    await AddTunnelChannel(streamConnection, hostEndPoint, filterGeneration, cancellationToken).Vhc();
                    _stat.TcpTunnelledCount++;
                    break;
            }
        }
        finally {
            Interlocked.Decrement(ref _processingCount);
        }
    }

    // A connection decided by rules that changed since does not go out: the change ended the connections open then
    // (ClientSession), and this one, still being set up, would go on by the old rules
    private void ThrowIfRulesChanged(int filterGeneration)
    {
        if (session.FilterGeneration != filterGeneration)
            throw new NetFilterException("The filter rules changed while the connection was decided.");
    }

    private async Task AddPassthruChannel(IStreamConnection streamConnection, IPEndPoint hostEndPoint,
        int filterGeneration, CancellationToken cancellationToken)
    {
        // apply NetMapper
        if (netFilter.IpMapper?.ToHost(IpProtocol.Tcp, hostEndPoint.ToValue(), out var newEndPoint) == true)
            hostEndPoint = newEndPoint.ToIPEndPoint();

        // check IPv6 support: dropped as its packets are, no fault to log as one
        if (hostEndPoint.IsV6 && !session.Status.IsIpV6SupportedByClient)
            throw new NetFilterException("IPv6 is not supported by client.");

        //log
        VhLogger.Instance.LogDebug(GeneralEventId.StreamForwardingChannel,
            "Adding a new stream channel to bypass. HostEp: {HostEp}, ConnectionId: {ConnectionId}",
            VhLogger.Format(hostEndPoint), streamConnection.ConnectionId);

        // set timeout
        using var timeoutCts = new CancellationTokenSource(tcpConnectTimeout);
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

        // connect to host
        ThrowIfRulesChanged(filterGeneration);
        var tcpClient = socketFactory.CreateTcpClient(hostEndPoint);
        try {
            await tcpClient.ConnectAsync(hostEndPoint.Address, hostEndPoint.Port, connectCts.Token).Vhc();
            var hostConnection = new TcpStreamConnection(tcpClient, connectionId: streamConnection.ConnectionId, connectionName: "host", isServer: false);

            try {
                // create and add the channel, unless the rules changed while the host was reached: the bytes read
                // for the name would leave with it
                session.AddChannelUnlessRulesChanged(filterGeneration, () => {
                    var channel = new StreamForwardingChannel(hostConnection.ToString(), streamConnection, hostConnection, streamForwardingBufferSize);
                    forwardingManager.AddChannel(channel, disposeOnFail: true);
                });
            }
            catch {
                await hostConnection.DisposeAsync().Vhc();
                throw;
            }
        }
        catch {
            tcpClient.Dispose();
            throw;
        }

    }

    private async Task AddTunnelChannel(
        IStreamConnection streamConnection, IPEndPoint hostEndPoint, int filterGeneration,
        CancellationToken cancellationToken)
    {
        // dropped as its packets are, no fault to log as one: a name that the rules include comes here where the IP
        // gates send its address around the tunnel, as the TCP host takes such a connection
        if (hostEndPoint.IsV6 && !session.Status.IsIpV6SupportedByServer)
            throw new NetFilterException("IPv6 is not supported by server.");

        //log
        VhLogger.Instance.LogDebug(GeneralEventId.StreamForwardingChannel,
            "Adding a new stream channel to tunnel. HostEp: {HostEp}, ConnectionId: {ConnectionId}",
            VhLogger.Format(hostEndPoint), streamConnection.ConnectionId);


        // Create the Request
        var request = new TcpForwardingChannelRequest {
            RequestId = streamConnection.ConnectionId,
            SessionId = sessionId,
            SessionKey = sessionKey,
            DestinationEndPoint = hostEndPoint
        };

        // handle small buffer for tiny TLS hello or small HTTP request to remove bidirectional pattern
        // wait just 100ms, because is some case such as FTPS with explicit encryption, client waits for server data
        // before sending the TLS hello, so there is no initial data to prefetch
        var initContents = await ReadInitContents(streamConnection.Stream, TimeSpan.FromMilliseconds(100), cancellationToken).Vhc();

        // they leave with the request, outside the forwarding channel, so the speed limit is paid for them here
        if (initContents.Length > 0)
            await trafficMeter.ThrottleSendAsync(initContents.Length, cancellationToken).Vhc();

        // read the response
        ThrowIfRulesChanged(filterGeneration);
        var requestEx = new ClientRequestEx { Request = request, PostBuffer = initContents };
        var requestResult = await session.SendRequest<SessionResponse>(requestEx, cancellationToken).Vhc();
        try {
            var forwardingConnection = requestResult.StreamConnection;

            // account for prefetched init contents that bypassed the forwarding channel
            if (initContents.Length > 0)
                trafficMeter.OnSent(initContents.Length);

            // add the stream forwarding channel, unless the rules changed while the request was answered
            session.AddChannelUnlessRulesChanged(filterGeneration, () => {
                var channel = new StreamForwardingChannel(forwardingConnection.ToString()!, streamConnection, forwardingConnection, streamForwardingBufferSize, trafficMeter);
                tunnel.AddChannel(channel, disposeIfFailed: true);
            });
        }
        catch {
            // not waited for: the close may wait for the peer, and the app's connection would stay open meanwhile
            _ = requestResult.TryDisposeAsync();
            throw;
        }
    }

    // return init contents if exist in given timeout, otherwise return empty. 
    private static async Task<ReadOnlyMemory<byte>> ReadInitContents(Stream stream, TimeSpan initTimeout, CancellationToken cancellationToken)
    {
        using var prefetchCts = new CancellationTokenSource(initTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(prefetchCts.Token, cancellationToken);

        try {
            // handle small buffer for tiny TLS hello or small HTTP request to remove bidirectional pattern
            // wait just 100ms, because is some case such as FTPS with explicit encryption, client waits for server data
            // before sending the TLS hello, so there is no initial data to prefetch
            var memory = new Memory<byte>(new byte[TransportDefaults.PrefetchStreamBufferSize]);
            var read = await stream.ReadAsync(memory, linkedCts.Token).Vhc();
            var initContents = memory[..read];
            return initContents;
        }
        catch (OperationCanceledException) when (prefetchCts.IsCancellationRequested) {
            return ReadOnlyMemory<byte>.Empty;
        }
    }

    private async Task<(IStreamConnection Connection, FilterAction filterAction)> ApplySniFiltering(
        IStreamConnection streamConnection, IPEndPoint hostEndPoint, CancellationToken cancellationToken)
    {
        // Filter by SNI (it log by its own observer, no need to log here). A name that cannot be read drops the
        // connection as a rule's block does: the filter's outcome, no fault to log as one
        StreamSniFilterResult filterResult;
        try {
            filterResult = await domainFilterService.ProcessStream(
                streamConnection.Stream, hostEndPoint, cancellationToken).Vhc();
        }
        catch (Exception ex) when (ex is TimeoutException or InvalidDataException) {
            throw new NetFilterException(ex.Message, ex);
        }

        // update connection stream with ReadBufferedStream to pre-append the read data
        if (filterResult.ReadData.Length > 0)
            streamConnection = new StreamConnectionDecorator(streamConnection,
                new ReadBufferedStream(streamConnection.Stream, leaveOpen: false, filterResult.ReadData.Span) {
                    AllowBufferRefill = false
                });

        return (streamConnection, filterResult.Action);
    }

    private class ClientHostStat : IClientHostStat
    {
        public int TcpTunnelledCount { get; set; }
        public int TcpPassthruCount { get; set; }
    }
}