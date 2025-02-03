﻿using System.Net;
using System.Net.Sockets;
using Ga4.Trackers;
using Ga4.Trackers.Ga4Tags;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Core.Common.Trackers;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.ConnectorServices;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Client.Device.Exceptions;
using VpnHood.Core.Client.DomainFiltering;
using VpnHood.Core.Client.Exceptions;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Jobs;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Net;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Common.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.ClientStreams;
using VpnHood.Core.Tunneling.Factory;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.Utils;
using PacketReceivedEventArgs = VpnHood.Core.Client.Device.PacketReceivedEventArgs;
using ProtocolType = PacketDotNet.ProtocolType;

namespace VpnHood.Core.Client;

public class VpnHoodClient : IJob, IAsyncDisposable
{
    private const int MaxProtocolVersion = 6;
    private const int MinProtocolVersion = 4;
    private bool _disposed;
    private readonly bool _autoDisposePacketCapture;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly ClientProxyManager _proxyManager;
    private readonly Dictionary<IPAddress, bool> _includeIps = new();
    private readonly int _maxDatagramChannelCount;
    private readonly IPacketCapture _packetCapture;
    private readonly SendingPackets _sendingPackets = new();
    private readonly ClientHost _clientHost;
    private readonly SemaphoreSlim _datagramChannelsSemaphore = new(1, 1);
    private readonly IAdService? _adService;
    private readonly TimeSpan _minTcpDatagramLifespan;
    private readonly TimeSpan _maxTcpDatagramLifespan;
    private readonly bool _allowAnonymousTracker;
    private readonly ITracker? _usageTracker;
    private IPAddress[] _dnsServersIpV4 = [];
    private IPAddress[] _dnsServersIpV6 = [];
    private IPAddress[] _dnsServers = [];
    private ClientUsageTracker? _clientUsageTracker;
    private DateTime? _initConnectedTime;
    private DateTime? _lastConnectionErrorTime;
    private byte[]? _sessionKey;
    private bool _useUdpChannel;
    private ClientState _state = ClientState.None;
    private bool _isWaitingForAd;
    private ConnectorService? _connectorService;
    private readonly TimeSpan _tcpConnectTimeout;
    private DateTime? _autoWaitTime;
    private readonly ServerFinder _serverFinder;
    private readonly ConnectPlanId _planId;
    private readonly string? _accessCode;
    private readonly TimeSpan _canExtendByRewardedAdThreshold;
    private bool _isTunProviderSupported;
    private bool _isDnsServersAccepted;
    private readonly ConnectionInfo _connectionInfo = new();

    private ConnectorService ConnectorService => VhUtil.GetRequiredInstance(_connectorService);
    internal Nat Nat { get; }
    internal Tunnel Tunnel { get; }
    internal ClientSocketFactory SocketFactory { get; }
    public JobSection JobSection { get; } = new();
    public event EventHandler? StateChanged;
    public bool IsIpV6SupportedByServer { get; private set; }
    public bool IsIpV6SupportedByClient { get; internal set; }
    public TimeSpan SessionTimeout { get; set; }
    public TimeSpan AutoWaitTimeout { get; set; }
    public TimeSpan ReconnectTimeout { get; set; }
    public Token Token { get; }
    public string ClientId { get; }
    public ulong SessionId { get; private set; }
    public Version Version { get; }
    public bool IncludeLocalNetwork { get; }
    public IpRangeOrderedList IncludeIpRanges { get; private set; }
    public IpRangeOrderedList PacketCaptureIncludeIpRanges { get; private set; }
    public string UserAgent { get; }
    public IPEndPoint? HostTcpEndPoint => _connectorService?.EndPointInfo.TcpEndPoint;
    public IPEndPoint? HostUdpEndPoint { get; private set; }
    public bool DropUdp { get; set; }
    public bool DropQuic { get; set; }
    public bool UseTcpOverTun { get; set; }
    public IConnectionInfo ConnectionInfo => _connectionInfo;

    public byte[] SessionKey =>
        _sessionKey ?? throw new InvalidOperationException($"{nameof(SessionKey)} has not been initialized.");

    public byte[]? ServerSecret { get; private set; }
    public DomainFilterService DomainFilterService { get; }
    public bool AllowTcpReuse { get; }

    public VpnHoodClient(IPacketCapture packetCapture, ISocketFactory socketFactory, IAdService? adService,
        string clientId, Token token, ClientOptions options)
    {
        if (options.TcpProxyCatcherAddressIpV4 == null)
            throw new ArgumentNullException(nameof(options.TcpProxyCatcherAddressIpV4));

        if (options.TcpProxyCatcherAddressIpV6 == null)
            throw new ArgumentNullException(nameof(options.TcpProxyCatcherAddressIpV6));

        if (!VhUtil.IsInfinite(_maxTcpDatagramLifespan) && _maxTcpDatagramLifespan < _minTcpDatagramLifespan)
            throw new ArgumentNullException(nameof(options.MaxTcpDatagramTimespan),
                $"{nameof(options.MaxTcpDatagramTimespan)} must be bigger or equal than {nameof(options.MinTcpDatagramTimespan)}.");

        SocketFactory = new ClientSocketFactory(packetCapture, socketFactory);
        socketFactory = SocketFactory;
        DnsServers = options.DnsServers ?? [];
        IncludeIpRanges = options.IncludeIpRanges;
        _allowAnonymousTracker = options.AllowAnonymousTracker;
        _minTcpDatagramLifespan = options.MinTcpDatagramTimespan;
        _maxTcpDatagramLifespan = options.MaxTcpDatagramTimespan;
        _packetCapture = packetCapture;
        _autoDisposePacketCapture = options.AutoDisposePacketCapture;
        _maxDatagramChannelCount = options.MaxDatagramChannelCount;
        _proxyManager = new ClientProxyManager(packetCapture, socketFactory, new ProxyManagerOptions());
        _usageTracker = options.Tracker;
        _tcpConnectTimeout = options.ConnectTimeout;
        _useUdpChannel = options.UseUdpChannel;
        _adService = adService;
        _planId = options.PlanId;
        _accessCode = options.AccessCode;
        _canExtendByRewardedAdThreshold = options.CanExtendByRewardedAdThreshold;
        _serverFinder = new ServerFinder(socketFactory, token.ServerToken,
            serverLocation: options.ServerLocation,
            serverQueryTimeout: options.ServerQueryTimeout,
            tracker: options.AllowEndPointTracker ? options.Tracker : null);

        AllowTcpReuse = options.AllowTcpReuse;
        ReconnectTimeout = options.ReconnectTimeout;
        AutoWaitTimeout = options.AutoWaitTimeout;
        Token = token;
        Version = options.Version;
        UserAgent = options.UserAgent;
        ClientId = clientId;
        SessionTimeout = options.SessionTimeout;
        IncludeLocalNetwork = options.IncludeLocalNetwork;
        DropUdp = options.DropUdp;
        DropQuic = options.DropQuic;
        UseTcpOverTun = options.UseTcpOverTun;
        DomainFilterService = new DomainFilterService(options.DomainFilter, options.ForceLogSni);
        var dnsRange = DnsServers.Select(x => new IpRange(x)).ToArray();
        PacketCaptureIncludeIpRanges = options.PacketCaptureIncludeIpRanges.Union(dnsRange);
        IncludeIpRanges = options.IncludeIpRanges.Union(dnsRange);

        // NAT
        Nat = new Nat(true);

        // Tunnel
        Tunnel = new Tunnel();
        Tunnel.PacketReceived += Tunnel_OnPacketReceived;

        // create proxy host
        _clientHost = new ClientHost(this, options.TcpProxyCatcherAddressIpV4, options.TcpProxyCatcherAddressIpV6);

        // init packetCapture cancellation
        packetCapture.Stopped += PacketCapture_OnStopped;
        packetCapture.PacketReceivedFromInbound += PacketCapture_OnPacketReceivedFromInbound;

        // Create simple disposable objects
        _cancellationTokenSource = new CancellationTokenSource();
        JobRunner.Default.Add(this);
    }


    public IPAddress[] DnsServers {
        get => _dnsServers;
        private set {
            _dnsServersIpV4 = value.Where(x => x.AddressFamily == AddressFamily.InterNetwork).ToArray();
            _dnsServersIpV6 = value.Where(x => x.AddressFamily == AddressFamily.InterNetworkV6).ToArray();
            _dnsServers = value;
        }
    }

    public ClientState State {
        get => _state;
        private set {
            if (_state == value) return;
            _connectionInfo.ClientState = value;
            _state = value; //must set before raising the event; 
            VhLogger.Instance.LogInformation("Client state is changed. NewState: {NewState}", State);
            try {
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex) {
                VhLogger.Instance.LogWarning(ex, "Could not fire client's StateChanged.");
            }
        }
    }

    private bool CanExtendByRewardedAd(AccessUsage? accessUsage)
    {
        return
            accessUsage is { CanExtendByRewardedAd: true, ExpirationTime: not null } &&
            accessUsage.ExpirationTime > FastDateTime.UtcNow + _canExtendByRewardedAdThreshold &&
            _packetCapture.CanDetectInProcessPacket &&
            _adService is { CanShowRewarded: true } &&
            Token.IsPublic;
    }


    public bool UseUdpChannel {
        get => _useUdpChannel;
        set {
            if (_useUdpChannel == value) return;
            _useUdpChannel = value;
            _ = ManageDatagramChannels(_cancellationTokenSource.Token);
        }
    }

    private void PacketCapture_OnStopped(object sender, EventArgs e)
    {
        VhLogger.Instance.LogTrace("Device has been stopped.");
        _ = DisposeAsync(false);
    }

    internal async Task AddPassthruTcpStream(IClientStream orgTcpClientStream, IPEndPoint hostEndPoint,
        string channelId, byte[] initBuffer, CancellationToken cancellationToken)
    {
        // set timeout
        using var cancellationTokenSource = new CancellationTokenSource(ConnectorService.RequestTimeout);
        using var linkedCancellationTokenSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationTokenSource.Token, cancellationToken);
        cancellationToken = linkedCancellationTokenSource.Token;

        // connect to host
        var tcpClient = SocketFactory.CreateTcpClient(hostEndPoint.AddressFamily);
        await VhUtil.RunTask(tcpClient.ConnectAsync(hostEndPoint.Address, hostEndPoint.Port),
            cancellationToken: cancellationToken).VhConfigureAwait();

        // create and add the channel
        var bypassChannel = new StreamProxyChannel(channelId, orgTcpClientStream,
            new TcpClientStream(tcpClient, tcpClient.GetStream(), channelId + ":host"));

        // flush initBuffer
        await tcpClient.GetStream().WriteAsync(initBuffer, cancellationToken);

        try {
            _proxyManager.AddChannel(bypassChannel);
        }
        catch {
            await bypassChannel.DisposeAsync().VhConfigureAwait();
            throw;
        }
    }

    public async Task Connect(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(VhLogger.FormatType(this));

        // merge cancellation tokens
        using var linkedCancellationTokenSource =
            CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, cancellationToken);
        cancellationToken = linkedCancellationTokenSource.Token;

        // create connection log scope
        using var scope = VhLogger.Instance.BeginScope("Client");
        if (State != ClientState.None)
            throw new Exception("Connection is already in progress.");

        // Connecting. Must before IsIpv6Supported
        State = ClientState.Connecting;

        // report config
        IsIpV6SupportedByClient = await IPAddressUtil.IsIpv6Supported();
        _serverFinder.IncludeIpV6 = IsIpV6SupportedByClient;
        ThreadPool.GetMinThreads(out var workerThreads, out var completionPortThreads);
        VhLogger.Instance.LogInformation(
            "UseUdpChannel: {UseUdpChannel}, DropUdp: {DropUdp}, DropQuic: {DropQuic}, UseTcpOverTun: {UseTcpOverTun}" +
            "IncludeLocalNetwork: {IncludeLocalNetwork}, MinWorkerThreads: {WorkerThreads}, " +
            "CompletionPortThreads: {CompletionPortThreads}, ClientIpV6: {ClientIpV6}",
            UseUdpChannel, DropUdp, DropQuic, UseTcpOverTun, IncludeLocalNetwork, workerThreads,
            completionPortThreads, IsIpV6SupportedByClient);

        // report version
        VhLogger.Instance.LogInformation(
            "ClientVersion: {ClientVersion}, " +
            "ClientMinProtocolVersion: {ClientMinProtocolVersion}, ClientMinProtocolVersion: {ClientMaxProtocolVersion}, " +
            "ClientId: {ClientId}",
            Version, MinProtocolVersion, MaxProtocolVersion, VhLogger.FormatId(ClientId));

        // Connect
        try {
            // Init hostEndPoint
            var endPointInfo = new ConnectorEndPointInfo {
                HostName = Token.ServerToken.HostName,
                TcpEndPoint = await _serverFinder.FindReachableServerAsync(cancellationToken).VhConfigureAwait(),
                CertificateHash = Token.ServerToken.CertificateHash
            };
            _connectorService = new ConnectorService(endPointInfo, SocketFactory, _tcpConnectTimeout,
                allowTcpReuse: AllowTcpReuse);

            // Establish first connection and create a session
            await ConnectInternal(cancellationToken).VhConfigureAwait();

            // Create Tcp Proxy Host
            _clientHost.Start();

            // Preparing device;
            if (_packetCapture.Started) //make sure it is not a shared packet capture
                throw new InvalidOperationException("PacketCapture should not be started before connect.");

            ConfigPacketFilter(ConnectorService.EndPointInfo.TcpEndPoint.Address);
            _packetCapture.StartCapture();

            // disable IncludeIpRanges if it contains all networks
            if (IncludeIpRanges.IsAll())
                IncludeIpRanges = [];

            State = ClientState.Connected;
            _initConnectedTime = DateTime.UtcNow;
        }
        catch (Exception ex) {
            // ReSharper disable once DisposeOnUsingVariable
            // clear before start new async task
            scope?.Dispose();
            _ = DisposeAsync(ex);
            throw;
        }
    }

    private void ConfigPacketFilter(IPAddress hostIpAddress)
    {
        // DnsServer
        if (_packetCapture.IsDnsServersSupported)
            _packetCapture.DnsServers = DnsServers;

        // Start with user PacketCaptureIncludeIpRanges
        var includeIpRanges = PacketCaptureIncludeIpRanges;

        // exclude server if ProtectSocket is not supported to prevent loop
        if (!_packetCapture.CanProtectSocket)
            includeIpRanges = includeIpRanges.Exclude(hostIpAddress);

        // exclude local networks
        if (!IncludeLocalNetwork)
            includeIpRanges = includeIpRanges.Exclude(IpNetwork.LocalNetworks.ToIpRanges());

        // Make sure CatcherAddress is included
        includeIpRanges = includeIpRanges.Union([
            new IpRange(_clientHost.CatcherAddressIpV4),
            new IpRange(_clientHost.CatcherAddressIpV6)
        ]);

        _packetCapture.IncludeNetworks = includeIpRanges.ToIpNetworks().ToArray(); //sort and unify
        VhLogger.Instance.LogInformation(
            $"PacketCapture Include Networks: {string.Join(", ", _packetCapture.IncludeNetworks.Select(VhLogger.Format))}");
    }

    // WARNING: Performance Critical!
    private void Tunnel_OnPacketReceived(object sender, ChannelPacketReceivedEventArgs e)
    {
        // manually manage DNS reply if DNS does not supported by _packetCapture
        if (!_packetCapture.IsDnsServersSupported)
            // ReSharper disable once ForCanBeConvertedToForeach
            for (var i = 0; i < e.IpPackets.Count; i++) {
                var ipPacket = e.IpPackets[i];
                UpdateDnsRequest(ipPacket, false);
            }

        _packetCapture.SendPacketToInbound(e.IpPackets);
    }

    // WARNING: Performance Critical!
    private void PacketCapture_OnPacketReceivedFromInbound(object sender, PacketReceivedEventArgs e)
    {
        // stop traffic if the client has been disposed
        if (_disposed || _initConnectedTime is null)
            return;

        try {
            lock
                (_sendingPackets) // this method should not be called in multi-thread, if so we need to allocate the list per call
            {
                _sendingPackets.Clear(); // prevent reallocation in this intensive event
                var tunnelPackets = _sendingPackets.TunnelPackets;
                var tcpHostPackets = _sendingPackets.TcpHostPackets;
                var passthruPackets = _sendingPackets.PassthruPackets;
                var proxyPackets = _sendingPackets.ProxyPackets;
                var droppedPackets = _sendingPackets.DroppedPackets;

                // ReSharper disable once ForCanBeConvertedToForeach
                for (var i = 0; i < e.IpPackets.Count; i++) {
                    var ipPacket = e.IpPackets[i];
                    if (_disposed) return;
                    var isIpV6 = ipPacket.DestinationAddress.IsV6();
                    var udpPacket = ipPacket.Protocol == ProtocolType.Udp ? ipPacket.Extract<UdpPacket>() : null;
                    var isDnsPacket = udpPacket?.DestinationPort == 53;

                    // DNS packet must go through tunnel even if it is not in range
                    if (isDnsPacket) {
                        // Drop IPv6 if not support
                        if (isIpV6 && !IsIpV6SupportedByServer) {
                            droppedPackets.Add(ipPacket);
                        }
                        else {
                            if (!_packetCapture.IsDnsServersSupported)
                                UpdateDnsRequest(ipPacket, true);

                            tunnelPackets.Add(ipPacket);
                        }
                    }

                    else if (_packetCapture.CanSendPacketToOutbound) {
                        if (!IsInIpRange(ipPacket.DestinationAddress))
                            passthruPackets.Add(ipPacket);

                        // Drop IPv6 if not support
                        else if (isIpV6 && !IsIpV6SupportedByServer)
                            droppedPackets.Add(ipPacket);

                        else if (ipPacket.Protocol == ProtocolType.Tcp) {
                            if (_isTunProviderSupported && UseTcpOverTun)
                                tunnelPackets.Add(ipPacket);
                            else
                                tcpHostPackets.Add(ipPacket);
                        }

                        else if (ipPacket.Protocol is ProtocolType.Icmp or ProtocolType.IcmpV6) {
                            if (IsIcmpControlMessage(ipPacket))
                                droppedPackets.Add(ipPacket);
                            else
                                tunnelPackets.Add(ipPacket);
                        }

                        else if (ipPacket.Protocol is ProtocolType.Udp && ShouldTunnelUdpPacket(udpPacket!))
                            tunnelPackets.Add(ipPacket);

                        else
                            droppedPackets.Add(ipPacket);
                    }
                    else {
                        // tcp already check for InInRange and IpV6 and Proxy
                        if (ipPacket.Protocol == ProtocolType.Tcp) {
                            if (_isTunProviderSupported && UseTcpOverTun && IsInIpRange(ipPacket.DestinationAddress))
                                tunnelPackets.Add(ipPacket);
                            else
                                tcpHostPackets.Add(ipPacket);
                        }

                        // Drop IPv6 if not support
                        else if (isIpV6 && !IsIpV6SupportedByServer) {
                            if (!IsInIpRange(ipPacket.DestinationAddress))
                                proxyPackets.Add(ipPacket);
                            else
                                droppedPackets.Add(ipPacket);
                        }

                        // ICMP packet must go through tunnel because PingProxy does not support protect socket
                        else if (ipPacket.Protocol is ProtocolType.Icmp or ProtocolType.IcmpV6) {
                            if (IsIcmpControlMessage(ipPacket))
                                droppedPackets
                                    .Add(ipPacket); // ICMP can not be proxied so we don't need to check InInRange
                            else
                                tunnelPackets.Add(ipPacket);
                        }

                        // Udp
                        else if (ipPacket.Protocol == ProtocolType.Udp && udpPacket != null) {
                            if (!IsInIpRange(ipPacket.DestinationAddress) || _clientHost.ShouldPassthru(ipPacket,
                                    udpPacket.SourcePort, udpPacket.DestinationPort))
                                proxyPackets.Add(ipPacket);
                            else if (!ShouldTunnelUdpPacket(udpPacket))
                                droppedPackets.Add(ipPacket);
                            else
                                tunnelPackets.Add(ipPacket);
                        }
                        else
                            droppedPackets.Add(ipPacket);
                    }
                }

                // Stop tunnel traffics if the client is paused and unpause after AutoPauseTimeout
                if (_autoWaitTime != null) {
                    if (FastDateTime.Now - _autoWaitTime.Value < AutoWaitTimeout)
                        tunnelPackets.Clear();
                    else
                        _autoWaitTime = null;
                }

                // send packets
                if (tunnelPackets.Count > 0 && ShouldManageDatagramChannels)
                    _ = ManageDatagramChannels(_cancellationTokenSource.Token);

                if (tunnelPackets.Count > 0)
                    Tunnel.SendPackets(tunnelPackets, _cancellationTokenSource.Token);

                if (passthruPackets.Count > 0)
                    _packetCapture.SendPacketToOutbound(passthruPackets);

                if (proxyPackets.Count > 0)
                    _proxyManager.SendPackets(proxyPackets).Wait(_cancellationTokenSource.Token);

                if (tcpHostPackets.Count > 0)
                    _packetCapture.SendPacketToInbound(_clientHost.ProcessOutgoingPacket(tcpHostPackets));
            }

            // set state outside the lock as it may raise an event
            if (_autoWaitTime == null && State == ClientState.Waiting)
                State = ClientState.Connecting;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not process the captured packets.");
        }
    }

    private bool ShouldTunnelUdpPacket(UdpPacket udpPacket)
    {
        if (DropUdp)
            return false;

        if (DropQuic && PacketUtil.IsQuicPort(udpPacket))
            return false;

        return true;
    }

    private static bool IsIcmpControlMessage(IPPacket ipPacket)
    {
        switch (ipPacket) {
            // IPv4
            case { Version: IPVersion.IPv4, Protocol: ProtocolType.Icmp }: {
                    var icmpPacket = ipPacket.Extract<IcmpV4Packet>();
                    return icmpPacket.TypeCode != IcmpV4TypeCode.EchoRequest; // drop all other Icmp but echo
                }
            // IPv6
            case { Version: IPVersion.IPv6, Protocol: ProtocolType.IcmpV6 }: {
                    var icmpPacket = ipPacket.Extract<IcmpV6Packet>();
                    return icmpPacket.Type != IcmpV6Type.EchoRequest;
                }
            default:
                return false;
        }
    }

    public bool IsInIpRange(IPAddress ipAddress)
    {
        // all IPs are included if there is no filter
        if (IncludeIpRanges.Count == 0)
            return true;

        // check tcp-loopback
        if (ipAddress.Equals(_clientHost.CatcherAddressIpV4) ||
            ipAddress.Equals(_clientHost.CatcherAddressIpV6))
            return true;

        // check the cache
        if (_includeIps.TryGetValue(ipAddress, out var isInRange))
            return isInRange;

        // check include
        isInRange = IncludeIpRanges.IsInRange(ipAddress);

        // cache the result
        // we don't need to keep that much ips in the cache
        if (_includeIps.Count > 0xFFFF) {
            VhLogger.Instance.LogInformation("Clearing IP filter cache!");
            _includeIps.Clear();
        }

        _includeIps.Add(ipAddress, isInRange);
        return isInRange;
    }

    // ReSharper disable once UnusedMethodReturnValue.Local
    private bool UpdateDnsRequest(IPPacket ipPacket, bool outgoing)
    {
        if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));
        if (ipPacket.Protocol != ProtocolType.Udp) return false;

        // find dns server
        var dnsServers = ipPacket.Version == IPVersion.IPv4 ? _dnsServersIpV4 : _dnsServersIpV6;
        if (dnsServers.Length == 0) {
            VhLogger.Instance.LogWarning(
                "There is no DNS server for this Address Family. AddressFamily : {AddressFamily}",
                ipPacket.DestinationAddress.AddressFamily);
            return false;
        }

        // manage DNS outgoing packet if requested DNS is not VPN DNS
        if (outgoing && Array.FindIndex(dnsServers, x => x.Equals(ipPacket.DestinationAddress)) == -1) {
            var udpPacket = PacketUtil.ExtractUdp(ipPacket);
            if (udpPacket.DestinationPort == 53) //53 is DNS port
            {
                var dnsServer = dnsServers[0];
                VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.Dns,
                    $"DNS request from {VhLogger.Format(ipPacket.SourceAddress)}:{udpPacket.SourcePort} to {VhLogger.Format(ipPacket.DestinationAddress)}, Map to: {VhLogger.Format(dnsServer)}");
                udpPacket.SourcePort = Nat.GetOrAdd(ipPacket).NatId;
                ipPacket.DestinationAddress = dnsServer;
                PacketUtil.UpdateIpPacket(ipPacket);
                return true;
            }
        }

        // manage DNS incoming packet from VPN DNS
        else if (!outgoing && Array.FindIndex(dnsServers, x => x.Equals(ipPacket.SourceAddress)) != -1) {
            var udpPacket = PacketUtil.ExtractUdp(ipPacket);
            var natItem = (NatItemEx?)Nat.Resolve(ipPacket.Version, ProtocolType.Udp, udpPacket.DestinationPort);
            if (natItem != null) {
                VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.Dns,
                    $"DNS reply to {VhLogger.Format(natItem.SourceAddress)}:{natItem.SourcePort}");
                ipPacket.SourceAddress = natItem.DestinationAddress;
                udpPacket.DestinationPort = natItem.SourcePort;
                PacketUtil.UpdateIpPacket(ipPacket);
                return true;
            }
        }

        return false;
    }

    private bool ShouldManageDatagramChannels {
        get {
            if (_disposed) return false;
            if (_datagramChannelsSemaphore.CurrentCount == 0) return false;
            if (UseUdpChannel != Tunnel.IsUdpMode) return true;
            return !UseUdpChannel && Tunnel.DatagramChannelCount < _maxDatagramChannelCount;
        }
    }

    private async Task ManageDatagramChannels(CancellationToken cancellationToken)
    {
        // ShouldManageDatagramChannels checks the semaphore count so it must be called before WaitAsync
        if (!ShouldManageDatagramChannels)
            return;

        if (!await _datagramChannelsSemaphore.WaitAsync(0, cancellationToken).VhConfigureAwait())
            return;

        try {
            // make sure only one UdpChannel exists for DatagramChannels if UseUdpChannel is on
            if (UseUdpChannel)
                await AddUdpChannel().VhConfigureAwait();
            else
                await AddTcpDatagramChannel(cancellationToken).VhConfigureAwait();
        }
        catch (Exception ex) {
            if (_disposed) return;
            VhLogger.LogError(GeneralEventId.DatagramChannel, ex, "Could not Manage DatagramChannels.");
        }
        finally {
            _datagramChannelsSemaphore.Release();
        }
    }

    private async Task AddUdpChannel()
    {
        if (HostTcpEndPoint == null)
            throw new InvalidOperationException($"{nameof(HostTcpEndPoint)} is not initialized!");
        if (VhUtil.IsNullOrEmpty(ServerSecret)) throw new Exception("ServerSecret has not been set.");
        if (VhUtil.IsNullOrEmpty(_sessionKey)) throw new Exception("Server UdpKey has not been set.");
        if (HostUdpEndPoint == null) {
            UseUdpChannel = false;
            throw new Exception("Server does not serve any UDP endpoint.");
        }

        var udpClient = SocketFactory.CreateUdpClient(HostTcpEndPoint.AddressFamily);
        var udpChannel = new UdpChannel(SessionId, _sessionKey, false, ConnectorService.ProtocolVersion);
        try {
            var udpChannelTransmitter = new ClientUdpChannelTransmitter(udpChannel, udpClient, ServerSecret);
            udpChannel.SetRemote(udpChannelTransmitter, HostUdpEndPoint);
            Tunnel.AddChannel(udpChannel);
        }
        catch {
            udpClient.Dispose();
            await udpChannel.DisposeAsync().VhConfigureAwait();
            UseUdpChannel = false;
            throw;
        }
    }

    private async Task ConnectInternal(CancellationToken cancellationToken, bool allowRedirect = true)
    {
        try {
            VhLogger.Instance.LogInformation("Connecting to the server...");

            // send hello request
            var clientInfo = new ClientInfo {
                ClientId = ClientId,
                ClientVersion = Version.ToString(3),
                ProtocolVersion = ConnectorService.ProtocolVersion,
                UserAgent = UserAgent
            };

            var request = new HelloRequest {
                RequestId = Guid.NewGuid() + ":client",
                EncryptedClientId = VhUtil.EncryptClientId(clientInfo.ClientId, Token.Secret),
                ClientInfo = clientInfo,
                TokenId = Token.TokenId,
                ServerLocation = _serverFinder.ServerLocation,
                PlanId = _planId,
                AccessCode = _accessCode,
                AllowRedirect = allowRedirect,
                IsIpV6Supported = IsIpV6SupportedByClient
            };

            await using var requestResult = await SendRequest<HelloResponse>(request, cancellationToken).VhConfigureAwait();
            var helloResponse = requestResult.Response;

#pragma warning disable CS0618 // Type or member is obsolete
            if (helloResponse is { MinProtocolVersion: 0, ServerProtocolVersion: 5 }) {
                helloResponse.MinProtocolVersion = 5;
                helloResponse.MaxProtocolVersion = 5;
            }
#pragma warning restore CS0618 // Type or member is obsolete

            if (helloResponse.MinProtocolVersion < MinProtocolVersion)
                throw new SessionException(SessionErrorCode.UnsupportedServer,
                    "The server is outdated and does not support by your app!");

            if (helloResponse.MaxProtocolVersion > MaxProtocolVersion)
                throw new SessionException(SessionErrorCode.UnsupportedServer,
                    "This app is outdated and does not support by the server!");

            // initialize the connector
            ConnectorService.Init(
                Math.Min(helloResponse.MaxProtocolVersion, MaxProtocolVersion),
                helloResponse.RequestTimeout,
                helloResponse.ServerSecret,
                helloResponse.TcpReuseTimeout);

            // log response
            VhLogger.Instance.LogInformation(GeneralEventId.Session,
                "Hurray! Client has been connected! " +
                $"SessionId: {VhLogger.FormatId(helloResponse.SessionId)}, " +
                $"ServerVersion: {helloResponse.ServerVersion}, " +
                $"ServerMinProtocolVersion: {helloResponse.MinProtocolVersion}, " +
                $"ServerMaxProtocolVersion: {helloResponse.MaxProtocolVersion}, " +
                $"CurrentProtocolVersion: {ConnectorService.ProtocolVersion}, " +
                $"ClientIp: {VhLogger.Format(helloResponse.ClientPublicAddress)}",
                $"IsTunProviderSupported: {helloResponse.IsTunProviderSupported}",
                $"ClientCountry: {helloResponse.ClientCountry}");

            // get session id
            SessionId = helloResponse.SessionId != 0
                ? helloResponse.SessionId
                : throw new Exception("Invalid SessionId!");
            _sessionKey = helloResponse.SessionKey;
            _isTunProviderSupported = helloResponse.IsTunProviderSupported;
            ServerSecret = helloResponse.ServerSecret;
            IsIpV6SupportedByServer = helloResponse.IsIpV6Supported;

            if (helloResponse.UdpPort > 0)
                HostUdpEndPoint = new IPEndPoint(ConnectorService.EndPointInfo.TcpEndPoint.Address,
                    helloResponse.UdpPort.Value);

            // PacketCaptureIpRanges
            if (!VhUtil.IsNullOrEmpty(helloResponse.PacketCaptureIncludeIpRanges))
                PacketCaptureIncludeIpRanges =
                    PacketCaptureIncludeIpRanges.Intersect(helloResponse.PacketCaptureIncludeIpRanges);

            // IncludeIpRanges
            if (!VhUtil.IsNullOrEmpty(helloResponse.IncludeIpRanges) &&
                !helloResponse.IncludeIpRanges.ToOrderedList().IsAll())
                IncludeIpRanges = IncludeIpRanges.Intersect(helloResponse.IncludeIpRanges);

            // set DNS after setting IpFilters
            VhLogger.Instance.LogInformation("Configuring Client DNS servers... DnsServers: {DnsServers}",
                string.Join(", ", DnsServers.Select(x => x.ToString())));
            _isDnsServersAccepted =
                VhUtil.IsNullOrEmpty(DnsServers) || DnsServers.Any(IsInIpRange); // no servers means accept default
            if (!_isDnsServersAccepted)
                VhLogger.Instance.LogWarning(
                    "Client DNS servers have been ignored because the server does not route them.");

            DnsServers = DnsServers.Where(IsInIpRange).ToArray();
            if (VhUtil.IsNullOrEmpty(DnsServers)) {
                DnsServers = VhUtil.IsNullOrEmpty(helloResponse.DnsServers)
                    ? IPAddressUtil.GoogleDnsServers
                    : helloResponse.DnsServers;
                IncludeIpRanges = IncludeIpRanges.Union(DnsServers.Select(IpRange.FromIpAddress));
            }

            if (VhUtil.IsNullOrEmpty(DnsServers?.Where(IsInIpRange)
                    .ToArray())) // make sure there is at least one DNS server
                throw new Exception("Could not specify any DNS server. The server is not configured properly.");

            VhLogger.Instance.LogInformation("DnsServers: {DnsServers}",
                string.Join(", ", DnsServers.Select(VhLogger.Format)));

            // report Suppressed
            if (helloResponse.SuppressedTo == SessionSuppressType.YourSelf)
                VhLogger.Instance.LogWarning("You suppressed a session of yourself!");

            else if (helloResponse.SuppressedTo == SessionSuppressType.Other)
                VhLogger.Instance.LogWarning("You suppressed a session of another client!");

            // set the session info
            _connectionInfo.SessionInfo = new SessionInfo {
                ClientPublicIpAddress = helloResponse.ClientPublicAddress,
                ClientCountry = helloResponse.ClientCountry,
                AccessInfo = helloResponse.AccessInfo ?? new AccessInfo(),
                IsDnsServersAccepted = _isDnsServersAccepted,
                DnsServers = DnsServers,
                IsPremiumSession = helloResponse.AccessUsage?.IsPremium ?? false,
                IsUdpChannelSupported = HostUdpEndPoint != null,
                AccessKey = helloResponse.AccessKey,
                ServerVersion = Version.Parse(helloResponse.ServerVersion),
                SuppressedTo = helloResponse.SuppressedTo,
                ServerLocationInfo = helloResponse.ServerLocation != null
                    ? ServerLocationInfo.Parse(helloResponse.ServerLocation)
                    : null,
            };

            // set session status
            _connectionInfo.SessionStatus = new SessionStatus(this, helloResponse.AccessUsage ?? new AccessUsage());

            // show ad
            string? adNetworkName = null;
            if (helloResponse.AdRequirement is AdRequirement.Flexible)
                adNetworkName = await ShowNormalAd(cancellationToken).VhConfigureAwait();
            if (helloResponse.AdRequirement is AdRequirement.Rewarded)
                adNetworkName = await ShowRewardedAd(cancellationToken).VhConfigureAwait();

            // usage trackers
            if (_allowAnonymousTracker) {
                // Anonymous server usage tracker
                if (!string.IsNullOrEmpty(helloResponse.GaMeasurementId)) {
                    var ga4Tracking = new Ga4TagTracker {
                        SessionCount = 1,
                        MeasurementId = helloResponse.GaMeasurementId,
                        ClientId = ClientId,
                        SessionId = SessionId.ToString(),
                        UserAgent = UserAgent,
                        UserProperties = new Dictionary<string, object> { { "client_version", Version.ToString(3) } }
                    };

                    _ = ga4Tracking.Track(new Ga4TagEvent { EventName = TrackEventNames.SessionStart });
                }

                // Anonymous app usage tracker
                if (_usageTracker != null) {
                    _ = _usageTracker.Track(ClientTrackerBuilder.BuildConnectionSucceeded(
                        _serverFinder.ServerLocation,
                        isIpV6Supported: IsIpV6SupportedByClient,
                        hasRedirected: !allowRedirect,
                        endPoint: ConnectorService.EndPointInfo.TcpEndPoint,
                        adNetworkName: adNetworkName));

                    _clientUsageTracker = new ClientUsageTracker(_connectionInfo.SessionStatus, _usageTracker);
                }
            }

            // Preparing tunnel
            VhLogger.Instance.LogInformation("Configuring Datagram Channels...");
            Tunnel.MaxDatagramChannelCount = helloResponse.MaxDatagramChannelCount != 0
                ? Tunnel.MaxDatagramChannelCount =
                    Math.Min(_maxDatagramChannelCount, helloResponse.MaxDatagramChannelCount)
                : _maxDatagramChannelCount;

            // prepare packet capture
            _packetCapture.PrivateIpNetworks = helloResponse.PrivateIpNetworks;
            if (VhUtil.IsNullOrEmpty(helloResponse.PrivateIpNetworks)) {
                var ipNetworkV4 = new IpNetwork(IPAddress.Parse("10.8.0.2"), 32);
                var ipNetworkV6 = new IpNetwork(IPAddressUtil.GenerateUlaAddress(0x1001), 128);
                _packetCapture.PrivateIpNetworks = helloResponse.IsIpV6Supported
                    ? [ipNetworkV4, ipNetworkV6] 
                    : [ipNetworkV4];
            }

            // manage datagram channels
            await ManageDatagramChannels(cancellationToken).VhConfigureAwait();
        }
        catch (RedirectHostException ex) {
            if (!allowRedirect) {
                VhLogger.Instance.LogError(ex,
                    "The server replies with a redirect to another server again. We already redirected earlier. This is unexpected.");
                throw;
            }

            // todo: init new connector
            ConnectorService.EndPointInfo.TcpEndPoint =
                await _serverFinder.FindBestRedirectedServerAsync(ex.RedirectHostEndPoints, cancellationToken);

            await ConnectInternal(cancellationToken, false).VhConfigureAwait();
        }
    }

    private async Task AddTcpDatagramChannel(CancellationToken cancellationToken)
    {
        // Create and send the Request Message
        var request = new TcpDatagramChannelRequest {
            RequestId = Guid.NewGuid() + ":client",
            SessionId = SessionId,
            SessionKey = SessionKey
        };

        var requestResult = await SendRequest<SessionResponse>(request, cancellationToken).VhConfigureAwait();
        StreamDatagramChannel? channel = null;
        try {
            // find timespan
            var lifespan = !VhUtil.IsInfinite(_maxTcpDatagramLifespan)
                ? TimeSpan.FromSeconds(new Random().Next((int)_minTcpDatagramLifespan.TotalSeconds,
                    (int)_maxTcpDatagramLifespan.TotalSeconds))
                : Timeout.InfiniteTimeSpan;

            // add the new channel
            channel = new StreamDatagramChannel(requestResult.ClientStream, request.RequestId, lifespan);
            Tunnel.AddChannel(channel);
        }
        catch {
            if (channel != null) await channel.DisposeAsync().VhConfigureAwait();
            await requestResult.DisposeAsync().VhConfigureAwait();
            throw;
        }
    }

    internal async Task<ConnectorRequestResult<T>> SendRequest<T>(ClientRequest request,
        CancellationToken cancellationToken)
        where T : SessionResponse
    {
        if (_disposed)
            throw new ObjectDisposedException(VhLogger.FormatType(this));

        try {
            // create a connection and send the request 
            var requestResult = await ConnectorService.SendRequest<T>(request, cancellationToken).VhConfigureAwait();
            ((SessionStatus?)_connectionInfo.SessionStatus)?.Update(requestResult.Response.AccessUsage);

            // client is disposed meanwhile
            if (_disposed) {
                _ = requestResult.DisposeAsync();
                throw new ObjectDisposedException(VhLogger.FormatType(this));
            }

            _lastConnectionErrorTime = null;
            State = ClientState.Connected;
            return requestResult;
        }
        catch (SessionException ex) {
            ((SessionStatus?)_connectionInfo.SessionStatus)?.Update(ex.SessionResponse.AccessUsage);

            // SessionException means that the request accepted by server but there is an error for that request
            _lastConnectionErrorTime = null;

            // close session if server has ended the session
            if (ex.SessionResponse.ErrorCode != SessionErrorCode.GeneralError &&
                ex.SessionResponse.ErrorCode != SessionErrorCode.RedirectHost &&
                ex.SessionResponse.ErrorCode != SessionErrorCode.RewardedAdRejected) {
                _ = DisposeAsync(ex);
            }

            throw;
        }
        catch (UnauthorizedAccessException ex) {
            _ = DisposeAsync(ex);
            throw;
        }
        catch (Exception ex) {
            if (_disposed)
                throw;

            var now = FastDateTime.Now;
            _lastConnectionErrorTime ??= now;

            // dispose by session timeout and must before pause because SessionTimeout is bigger than ReconnectTimeout
            if (now - _lastConnectionErrorTime.Value > SessionTimeout)
                _ = DisposeAsync(ex);

            // pause after retry limit
            else if (now - _lastConnectionErrorTime.Value > ReconnectTimeout) {
                _autoWaitTime = now;
                State = ClientState.Waiting;
                VhLogger.Instance.LogWarning(ex, "Client is paused because of too many connection errors.");
            }

            // set connecting state if it could not establish any connection
            else if (State == ClientState.Connected)
                State = ClientState.Connecting;

            throw;
        }
    }

    public async Task UpdateSessionStatus(CancellationToken cancellationToken = default)
    {
        using var linkedCancellationTokenSource =
            CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, cancellationToken);
        cancellationToken = linkedCancellationTokenSource.Token;

        // don't use SendRequest because it can be disposed
        await using var requestResult = await SendRequest<SessionResponse>(
                new SessionStatusRequest() {
                    RequestId = Guid.NewGuid() + ":client",
                    SessionId = SessionId,
                    SessionKey = SessionKey
                },
                cancellationToken)
            .VhConfigureAwait();
    }

    private async Task SendByeRequest(CancellationToken cancellationToken)
    {
        try {
            // don't use SendRequest because it can be disposed
            await using var requestResult = await ConnectorService.SendRequest<SessionResponse>(
                    new ByeRequest {
                        RequestId = Guid.NewGuid() + ":client",
                        SessionId = SessionId,
                        SessionKey = SessionKey
                    },
                    cancellationToken)
                .VhConfigureAwait();
        }
        catch (Exception ex) {
            VhLogger.LogError(GeneralEventId.Session, ex, "Could not send the bye request.");
        }
    }

    public async Task<string?> ShowRewardedAd(CancellationToken cancellationToken)
    {
        try {
            if (_adService == null)
                throw new InvalidOperationException("Client's AdService has not been initialized.");

            if (SessionId == 0)
                throw new InvalidOperationException("Session has not been established.");

            _isWaitingForAd = true;
            _clientHost.PassthruInProcessPackets = true;
            var adResult = await _adService
                .ShowRewarded(ActiveUiContext.RequiredContext, SessionId.ToString(), cancellationToken)
                .VhConfigureAwait();

            if (!string.IsNullOrEmpty(adResult.AdData))
                await SendRewardedAd(adResult.AdData, cancellationToken);

            return adResult.NetworkName;
        }
        catch (UiContextNotAvailableException) {
            throw new ShowAdNoUiException();
        }
        finally {
            _isWaitingForAd = false;
            _clientHost.PassthruInProcessPackets = false;
        }
    }

    /// <returns>NetworkName</returns>
    public async Task<string?> ShowNormalAd(CancellationToken cancellationToken)
    {
        try {
            if (_adService == null)
                throw new InvalidOperationException("Client's AdService has not been initialized.");

            if (SessionId == 0)
                throw new InvalidOperationException("Session has not been established.");

            _isWaitingForAd = true;
            _clientHost.PassthruInProcessPackets = true;
            var adResult = await _adService
                .ShowInterstitial(ActiveUiContext.RequiredContext, SessionId.ToString(), cancellationToken)
                .VhConfigureAwait();

            return adResult.NetworkName;
        }
        catch (UiContextNotAvailableException) {
            throw new ShowAdNoUiException();
        }
        catch (LoadAdException ex) {

            VhLogger.Instance.LogInformation(ex, "Could not load any ad.");
            // ignore exception for flexible ad if load failed
            return null;
        }
        finally {
            _isWaitingForAd = false;
            _clientHost.PassthruInProcessPackets = false;

        }
    }

    public Task RunJob()
    {
        if (_disposed)
            return Task.CompletedTask;

        if (FastDateTime.UtcNow > _connectionInfo.SessionStatus?.SessionExpirationTime)
            _ = DisposeAsync(new SessionException(SessionErrorCode.AccessExpired));

        return Task.CompletedTask;
    }

    private async Task SendRewardedAd(string adData, CancellationToken cancellationToken)
    {
        try {
            // request reward from server
            await using var requestResult = await SendRequest<SessionResponse>(
                    new RewardedAdRequest {
                        RequestId = Guid.NewGuid() + ":client",
                        SessionId = SessionId,
                        SessionKey = SessionKey,
                        AdData = adData
                    },
                    cancellationToken)
                .VhConfigureAwait();
        }
        catch (Exception ex) {
            VhLogger.LogError(GeneralEventId.Session, ex, "Could not send the RewardedAd request.");
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        return DisposeAsync(false);
    }

    private async ValueTask DisposeAsync(Exception ex)
    {
        _connectionInfo.SetException(ex);
        await DisposeAsync(false);
    }

    private readonly AsyncLock _disposeLock = new();

    public async ValueTask DisposeAsync(bool waitForBye)
    {
        using var lockResult = await _disposeLock.LockAsync(TimeSpan.Zero).VhConfigureAwait();
        if (_disposed || !lockResult.Succeeded) return;
        _disposed = true;

        // shutdown
        VhLogger.Instance.LogInformation("Shutting down...");

        _cancellationTokenSource.Cancel();
        var wasConnected = State is ClientState.Connecting or ClientState.Connected;
        State = ClientState.Disconnecting;

        // disposing PacketCapture. Must be at end for graceful shutdown
        _packetCapture.Stopped -= PacketCapture_OnStopped;
        _packetCapture.PacketReceivedFromInbound -= PacketCapture_OnPacketReceivedFromInbound;
        if (_autoDisposePacketCapture) {
            VhLogger.Instance.LogTrace("Disposing the PacketCapture...");
            _packetCapture.Dispose();
        }

        var finalizeTask = Finalize(wasConnected);
        if (waitForBye)
            await finalizeTask.VhConfigureAwait();
    }

    private async Task Finalize(bool wasConnected)
    {
        // dispose job runner (not required)
        JobRunner.Default.Remove(this);

        // Anonymous usage tracker
        _ = _clientUsageTracker?.DisposeAsync();

        VhLogger.Instance.LogTrace("Disposing ClientHost...");
        await _clientHost.DisposeAsync().VhConfigureAwait();

        // Tunnel
        VhLogger.Instance.LogTrace("Disposing Tunnel...");
        Tunnel.PacketReceived -= Tunnel_OnPacketReceived;
        await Tunnel.DisposeAsync().VhConfigureAwait();

        VhLogger.Instance.LogTrace("Disposing ProxyManager...");
        await _proxyManager.DisposeAsync().VhConfigureAwait();

        // dispose NAT
        VhLogger.Instance.LogTrace("Disposing Nat...");
        Nat.Dispose();

        // Sending Bye
        if (wasConnected && SessionId != 0 && _connectionInfo.ErrorCode == SessionErrorCode.Ok) {
            using var cancellationTokenSource = new CancellationTokenSource(TunnelDefaults.TcpGracefulTimeout);
            await SendByeRequest(cancellationTokenSource.Token).VhConfigureAwait();
        }

        // dispose ConnectorService
        VhLogger.Instance.LogTrace("Disposing ConnectorService...");
        await ConnectorService.DisposeAsync().VhConfigureAwait();

        State = ClientState.Disposed;
        VhLogger.Instance.LogInformation("Bye Bye!");
    }

    private class SendingPackets
    {
        public readonly List<IPPacket> PassthruPackets = [];
        public readonly List<IPPacket> ProxyPackets = [];
        public readonly List<IPPacket> TcpHostPackets = [];
        public readonly List<IPPacket> TunnelPackets = [];
        public readonly List<IPPacket> DroppedPackets = [];

        public void Clear()
        {
            TunnelPackets.Clear();
            PassthruPackets.Clear();
            TcpHostPackets.Clear();
            ProxyPackets.Clear();
            DroppedPackets.Clear();
        }
    }

    private class SessionStatus(VpnHoodClient client, AccessUsage accessUsage) : ISessionStatus
    {
        private AccessUsage _accessUsage = accessUsage;
        private readonly Traffic _cycleTraffic = accessUsage.CycleTraffic;
        private readonly Traffic _totalTraffic = accessUsage.TotalTraffic;

        public void Update(AccessUsage? value) => _accessUsage = value ?? _accessUsage;
        public ConnectorStat ConnectorStat => client.ConnectorService.Stat;
        public Traffic Speed => client.Tunnel.Speed;
        public Traffic SessionTraffic => client.Tunnel.Traffic;
        public Traffic CycleTraffic => _cycleTraffic + client.Tunnel.Traffic;
        public Traffic TotalTraffic => _totalTraffic + client.Tunnel.Traffic;
        public int TcpTunnelledCount => client._clientHost.Stat.TcpTunnelledCount;
        public int TcpPassthruCount => client._clientHost.Stat.TcpPassthruCount;
        public int DatagramChannelCount => client.Tunnel.DatagramChannelCount;
        public bool IsUdpMode => client.Tunnel.IsUdpMode;
        public bool IsWaitingForAd => client._isWaitingForAd;
        public bool CanExtendByRewardedAd => client.CanExtendByRewardedAd(_accessUsage);
        public long SessionMaxTraffic => _accessUsage.MaxTraffic;
        public DateTime? SessionExpirationTime => _accessUsage.ExpirationTime;
        public int? ActiveClientCount => _accessUsage.ActiveClientCount;
    }

}