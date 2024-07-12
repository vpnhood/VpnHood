using System.Net;
using System.Net.Sockets;
using Ga4.Trackers;
using Ga4.Trackers.Ga4Tags;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Client.Abstractions;
using VpnHood.Client.ConnectorServices;
using VpnHood.Client.Device;
using VpnHood.Client.Exceptions;
using VpnHood.Common;
using VpnHood.Common.ApiClients;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;
using VpnHood.Common.Trackers;
using VpnHood.Common.Utils;
using VpnHood.Tunneling;
using VpnHood.Tunneling.Channels;
using VpnHood.Tunneling.ClientStreams;
using VpnHood.Tunneling.DomainFiltering;
using VpnHood.Tunneling.Messaging;
using VpnHood.Tunneling.Utils;
using PacketReceivedEventArgs = VpnHood.Client.Device.PacketReceivedEventArgs;
using ProtocolType = PacketDotNet.ProtocolType;

namespace VpnHood.Client;

public class VpnHoodClient : IDisposable, IAsyncDisposable
{
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
    private readonly IIpRangeProvider? _ipRangeProvider;
    private readonly IAdProvider? _adProvider;
    private readonly TimeSpan _minTcpDatagramLifespan;
    private readonly TimeSpan _maxTcpDatagramLifespan;
    private readonly bool _allowAnonymousTracker;
    private readonly ITracker? _usageTracker;
    private IPAddress[] _dnsServersIpV4 = [];
    private IPAddress[] _dnsServersIpV6 = [];
    private IPAddress[] _dnsServers = [];
    private Traffic _helloTraffic = new();
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
    private ConnectorService ConnectorService => VhUtil.GetRequiredInstance(_connectorService);
    private int ProtocolVersion { get; }
    internal Nat Nat { get; }
    internal Tunnel Tunnel { get; }
    internal ClientSocketFactory SocketFactory { get; }

    public event EventHandler? StateChanged;
    public Version? ServerVersion { get; private set; }
    public IPAddress? PublicAddress { get; private set; }
    public bool IsIpV6SupportedByServer { get; private set; }
    public bool IsIpV6SupportedByClient { get; internal set; }
    public TimeSpan SessionTimeout { get; set; }
    public TimeSpan AutoWaitTimeout { get; set; }
    public TimeSpan ReconnectTimeout { get; set; }
    public Token Token { get; }
    public Guid ClientId { get; }
    public ulong SessionId { get; private set; }
    public SessionStatus SessionStatus { get; private set; } = new();
    public Version Version { get; }
    public bool IncludeLocalNetwork { get; }
    public IpRangeOrderedList IncludeIpRanges { get; private set; } = new(IpNetwork.All.ToIpRanges());
    public IpRangeOrderedList PacketCaptureIncludeIpRanges { get; private set; }
    public string UserAgent { get; }
    public IPEndPoint? HostTcpEndPoint => _connectorService?.EndPointInfo.TcpEndPoint;
    public IPEndPoint? HostUdpEndPoint { get; private set; }
    public bool DropUdpPackets { get; set; }
    public ClientStat Stat { get; }
    public byte[] SessionKey => _sessionKey ?? throw new InvalidOperationException($"{nameof(SessionKey)} has not been initialized.");
    public byte[]? ServerSecret { get; private set; }
    public string? ResponseAccessKey { get; private set; }
    public DomainFilterService DomainFilterService { get; }

    public VpnHoodClient(IPacketCapture packetCapture, Guid clientId, Token token, ClientOptions options)
    {
        if (options.TcpProxyCatcherAddressIpV4 == null)
            throw new ArgumentNullException(nameof(options.TcpProxyCatcherAddressIpV4));

        if (options.TcpProxyCatcherAddressIpV6 == null)
            throw new ArgumentNullException(nameof(options.TcpProxyCatcherAddressIpV6));

        if (!VhUtil.IsInfinite(_maxTcpDatagramLifespan) && _maxTcpDatagramLifespan < _minTcpDatagramLifespan)
            throw new ArgumentNullException(nameof(options.MaxTcpDatagramTimespan), $"{nameof(options.MaxTcpDatagramTimespan)} must be bigger or equal than {nameof(options.MinTcpDatagramTimespan)}.");

        SocketFactory = new ClientSocketFactory(packetCapture, options.SocketFactory ?? throw new ArgumentNullException(nameof(options.SocketFactory)));
        DnsServers = options.DnsServers ?? [];
        _allowAnonymousTracker = options.AllowAnonymousTracker;
        _minTcpDatagramLifespan = options.MinTcpDatagramTimespan;
        _maxTcpDatagramLifespan = options.MaxTcpDatagramTimespan;
        _packetCapture = packetCapture;
        _autoDisposePacketCapture = options.AutoDisposePacketCapture;
        _maxDatagramChannelCount = options.MaxDatagramChannelCount;
        _proxyManager = new ClientProxyManager(packetCapture, SocketFactory, new ProxyManagerOptions());
        _ipRangeProvider = options.IpRangeProvider;
        _usageTracker = options.Tracker;
        _tcpConnectTimeout = options.ConnectTimeout;
        _useUdpChannel = options.UseUdpChannel;
        _adProvider = options.AdProvider;
        _serverFinder = new ServerFinder(options.SocketFactory, token.ServerToken,
            serverLocation:  options.ServerLocation,
            serverQueryTimeout: options.ServerQueryTimeout,
            tracker: options.AllowEndPointTracker ? options.Tracker : null);

        ReconnectTimeout = options.ReconnectTimeout;
        AutoWaitTimeout = options.AutoWaitTimeout;
        Token = token;
        Version = options.Version;
        UserAgent = options.UserAgent;
        ProtocolVersion = 4;
        ClientId = clientId;
        SessionTimeout = options.SessionTimeout;
        IncludeLocalNetwork = options.IncludeLocalNetwork;
        PacketCaptureIncludeIpRanges = options.PacketCaptureIncludeIpRanges;
        DropUdpPackets = options.DropUdpPackets;
        DomainFilterService = new DomainFilterService(options.DomainFilter, options.ForceLogSni);

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
        Stat = new ClientStat(this);

#if DEBUG
        if (options.ProtocolVersion != 0) ProtocolVersion = options.ProtocolVersion;
#endif
    }

    public IPAddress[] DnsServers
    {
        get => _dnsServers;
        private set
        {
            _dnsServersIpV4 = value.Where(x => x.AddressFamily == AddressFamily.InterNetwork).ToArray();
            _dnsServersIpV6 = value.Where(x => x.AddressFamily == AddressFamily.InterNetworkV6).ToArray();
            _dnsServers = value;
        }
    }

    public ClientState State
    {
        get => _state;
        private set
        {
            if (_state == value) return;
            _state = value; //must set before raising the event; 
            VhLogger.Instance.LogInformation("Client state is changed. NewState: {NewState}", State);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool UseUdpChannel
    {
        get => _useUdpChannel;
        set
        {
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
        using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationTokenSource.Token, cancellationToken);
        cancellationToken = linkedCancellationTokenSource.Token;

        // connect to host
        var tcpClient = SocketFactory.CreateTcpClient(hostEndPoint.AddressFamily);
        await VhUtil.RunTask(tcpClient.ConnectAsync(hostEndPoint.Address, hostEndPoint.Port), cancellationToken: cancellationToken).VhConfigureAwait();

        // create and add the channel
        var bypassChannel = new StreamProxyChannel(channelId, orgTcpClientStream,
            new TcpClientStream(tcpClient, tcpClient.GetStream(), channelId + ":host"));

        // flush initBuffer
        await tcpClient.GetStream().WriteAsync(initBuffer, linkedCancellationTokenSource.Token);

        try { _proxyManager.AddChannel(bypassChannel); }
        catch { await bypassChannel.DisposeAsync().VhConfigureAwait(); throw; }
    }

    public async Task Connect(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(VhLogger.FormatType(this));

        using var scope = VhLogger.Instance.BeginScope("Client");
        if (State != ClientState.None)
            throw new Exception("Connection is already in progress.");


        // report config
        IsIpV6SupportedByClient = await IPAddressUtil.IsIpv6Supported();
        _serverFinder.IncludeIpV6 = IsIpV6SupportedByClient;
        ThreadPool.GetMinThreads(out var workerThreads, out var completionPortThreads);
        VhLogger.Instance.LogInformation(
            "UseUdpChannel: {UseUdpChannel}, DropUdpPackets: {DropUdpPackets}, IncludeLocalNetwork: {IncludeLocalNetwork}, " +
            "MinWorkerThreads: {WorkerThreads}, CompletionPortThreads: {CompletionPortThreads}, ClientIpV6: {ClientIpV6}",
            UseUdpChannel, DropUdpPackets, IncludeLocalNetwork, workerThreads, completionPortThreads, IsIpV6SupportedByClient);

        // report version
        VhLogger.Instance.LogInformation("ClientVersion: {ClientVersion}, ClientProtocolVersion: {ClientProtocolVersion}, ClientId: {ClientId}",
            Version, ProtocolVersion, VhLogger.FormatId(ClientId));

        // Starting
        State = ClientState.Connecting;
        SessionStatus = new SessionStatus();

        // Connect
        try
        {
            // Init hostEndPoint
            var endPointInfo = new ConnectorEndPointInfo
            {
                HostName = Token.ServerToken.HostName,
                TcpEndPoint = await _serverFinder.FindBestServerAsync(cancellationToken).VhConfigureAwait(),
                CertificateHash = Token.ServerToken.CertificateHash
            };
            _connectorService = new ConnectorService(endPointInfo, SocketFactory, _tcpConnectTimeout);

            // Establish first connection and create a session
            using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, cancellationToken);
            await ConnectInternal(linkedCancellationTokenSource.Token).VhConfigureAwait();

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
        catch (Exception ex)
        {
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

        // Built-in IpV6 support
        if (_packetCapture.IsAddIpV6AddressSupported)
            _packetCapture.AddIpV6Address = true; //lets block ipV6 if not supported

        // Start with user PacketCaptureIncludeIpRanges
        var includeIpRanges = PacketCaptureIncludeIpRanges;

        // exclude server if ProtectSocket is not supported to prevent loop
        if (!_packetCapture.CanProtectSocket)
            includeIpRanges = includeIpRanges.Exclude(hostIpAddress);

        // exclude local networks
        if (!IncludeLocalNetwork)
            includeIpRanges = includeIpRanges.Exclude(IpNetwork.LocalNetworks.ToIpRanges());

        // Make sure CatcherAddress is included
        includeIpRanges = includeIpRanges.Union(new[]
        {
            new IpRange(_clientHost.CatcherAddressIpV4),
            new IpRange(_clientHost.CatcherAddressIpV6)
        });

        _packetCapture.IncludeNetworks = includeIpRanges.ToIpNetworks().ToArray(); //sort and unify
        VhLogger.Instance.LogInformation($"PacketCapture Include Networks: {string.Join(", ", _packetCapture.IncludeNetworks.Select(x => x.ToString()))}");
    }

    // WARNING: Performance Critical!
    private void Tunnel_OnPacketReceived(object sender, ChannelPacketReceivedEventArgs e)
    {
        // manually manage DNS reply if DNS does not supported by _packetCapture
        if (!_packetCapture.IsDnsServersSupported)
            // ReSharper disable once ForCanBeConvertedToForeach
            for (var i = 0; i < e.IpPackets.Length; i++)
            {
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

        try
        {
            lock (_sendingPackets) // this method should not be called in multi-thread, if so we need to allocate the list per call
            {
                _sendingPackets.Clear(); // prevent reallocation in this intensive event
                var tunnelPackets = _sendingPackets.TunnelPackets;
                var tcpHostPackets = _sendingPackets.TcpHostPackets;
                var passthruPackets = _sendingPackets.PassthruPackets;
                var proxyPackets = _sendingPackets.ProxyPackets;
                var droppedPackets = _sendingPackets.DroppedPackets;

                // ReSharper disable once ForCanBeConvertedToForeach
                for (var i = 0; i < e.IpPackets.Count; i++)
                {
                    var ipPacket = e.IpPackets[i];
                    if (_disposed) return;
                    var isIpV6 = ipPacket.DestinationAddress.AddressFamily == AddressFamily.InterNetworkV6;
                    var udpPacket = ipPacket.Protocol == ProtocolType.Udp ? ipPacket.Extract<UdpPacket>() : null;
                    var isDnsPacket = udpPacket?.DestinationPort == 53;

                    // DNS packet must go through tunnel even if it is not in range
                    if (isDnsPacket)
                    {
                        // Drop IPv6 if not support
                        if (isIpV6 && !IsIpV6SupportedByServer)
                        {
                            droppedPackets.Add(ipPacket);
                        }
                        else
                        {
                            if (!_packetCapture.IsDnsServersSupported)
                                UpdateDnsRequest(ipPacket, true);

                            tunnelPackets.Add(ipPacket);
                        }
                    }

                    else if (_packetCapture.CanSendPacketToOutbound)
                    {
                        if (!IsInIpRange(ipPacket.DestinationAddress))
                            passthruPackets.Add(ipPacket);

                        // Drop IPv6 if not support
                        else if (isIpV6 && !IsIpV6SupportedByServer)
                            droppedPackets.Add(ipPacket);

                        // Check IPv6 control message such as Solicitations
                        else if (IsIcmpControlMessage(ipPacket))
                            droppedPackets.Add(ipPacket);

                        else if (ipPacket.Protocol == ProtocolType.Tcp)
                            tcpHostPackets.Add(ipPacket);

                        else if (ipPacket.Protocol is ProtocolType.Icmp or ProtocolType.IcmpV6)
                            tunnelPackets.Add(ipPacket);

                        else if (ipPacket.Protocol is ProtocolType.Udp && !DropUdpPackets)
                            tunnelPackets.Add(ipPacket);

                        else
                            droppedPackets.Add(ipPacket);
                    }
                    else
                    {
                        // Drop IPv6 if not support
                        if (isIpV6 && !IsIpV6SupportedByServer)
                            droppedPackets.Add(ipPacket);

                        // Check IPv6 control message such as Solicitations
                        else if (IsIcmpControlMessage(ipPacket))
                            droppedPackets.Add(ipPacket);

                        else if (ipPacket.Protocol == ProtocolType.Tcp)
                            tcpHostPackets.Add(ipPacket);

                        // ICMP packet must go through tunnel because PingProxy does not support protect socket
                        else if (ipPacket.Protocol is ProtocolType.Icmp or ProtocolType.IcmpV6)
                            tunnelPackets.Add(ipPacket);

                        // Udp
                        else if (ipPacket.Protocol == ProtocolType.Udp && !DropUdpPackets)
                        {
                            if (IsInIpRange(ipPacket.DestinationAddress)) //todo add InInProcess
                                tunnelPackets.Add(ipPacket);
                            else
                                proxyPackets.Add(ipPacket);
                        }

                        else
                            droppedPackets.Add(ipPacket);
                    }
                }

                // Stop tunnel traffics if the client is paused and unpause after AutoPauseTimeout
                if (_autoWaitTime != null)
                {
                    if (FastDateTime.Now - _autoWaitTime.Value < AutoWaitTimeout)
                        tunnelPackets.Clear();
                    else
                        _autoWaitTime = null;
                }

                // send packets
                if (tunnelPackets.Count > 0 && ShouldManageDatagramChannels) _ = ManageDatagramChannels(_cancellationTokenSource.Token);
                if (tunnelPackets.Count > 0) Tunnel.SendPackets(tunnelPackets, _cancellationTokenSource.Token);
                if (passthruPackets.Count > 0) _packetCapture.SendPacketToOutbound(passthruPackets);
                if (proxyPackets.Count > 0) _proxyManager.SendPackets(proxyPackets).Wait(_cancellationTokenSource.Token);
                if (tcpHostPackets.Count > 0) _packetCapture.SendPacketToInbound(_clientHost.ProcessOutgoingPacket(tcpHostPackets));
            }

            // set state outside the lock as it may raise an event
            if (_autoWaitTime == null && State == ClientState.Waiting)
                State = ClientState.Connecting;
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(ex, "Could not process the captured packets.");
        }
    }

    private static bool IsIcmpControlMessage(IPPacket ipPacket)
    {
        // IPv4
        if (ipPacket is { Version: IPVersion.IPv4, Protocol: ProtocolType.Icmp })
        {
            var icmpPacket = ipPacket.Extract<IcmpV4Packet>();
            return icmpPacket.TypeCode != IcmpV4TypeCode.EchoRequest; // drop all other Icmp but echo
        }

        // IPv6
        if (ipPacket is { Version: IPVersion.IPv6, Protocol: ProtocolType.IcmpV6 })
        {
            var icmpPacket = ipPacket.Extract<IcmpV6Packet>();
            if (icmpPacket.Type == IcmpV6Type.EchoRequest)
                return false;

            if (icmpPacket.Type == IcmpV6Type.NeighborSolicitation)
            {
                //_packetCapture.SendPacketToInbound(PacketUtil.CreateIcmpV6NeighborAdvertisement(ipPacket));
            }

            else if (icmpPacket.Type == IcmpV6Type.RouterSolicitation)
            {
                //_packetCapture.SendPacketToInbound(PacketUtil.CreateIcmpV6RouterAdvertisement(ipPacket));
            }

            return true; // drop all other Icmp but echo
        }

        return false;
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
        if (_includeIps.Count > 0xFFFF)
        {
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
        if (dnsServers.Length == 0)
        {
            VhLogger.Instance.LogWarning("There is no DNS server for this Address Family. AddressFamily : {AddressFamily}",
                ipPacket.DestinationAddress.AddressFamily);
            return false;
        }

        // manage DNS outgoing packet if requested DNS is not VPN DNS
        if (outgoing && Array.FindIndex(dnsServers, x => x.Equals(ipPacket.DestinationAddress)) == -1)
        {
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
        else if (!outgoing && Array.FindIndex(dnsServers, x => x.Equals(ipPacket.SourceAddress)) != -1)
        {
            var udpPacket = PacketUtil.ExtractUdp(ipPacket);
            var natItem = (NatItemEx?)Nat.Resolve(ipPacket.Version, ProtocolType.Udp, udpPacket.DestinationPort);
            if (natItem != null)
            {
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

    private bool ShouldManageDatagramChannels =>
        UseUdpChannel != Tunnel.IsUdpMode ||
        (!UseUdpChannel && Tunnel.DatagramChannelCount < _maxDatagramChannelCount);

    private async Task ManageDatagramChannels(CancellationToken cancellationToken)
    {
        if (_disposed || !await _datagramChannelsSemaphore.WaitAsync(0, cancellationToken).VhConfigureAwait())
            return;

        if (!ShouldManageDatagramChannels)
            return;

        try
        {
            // make sure only one UdpChannel exists for DatagramChannels if UseUdpChannel is on
            if (UseUdpChannel)
                await AddUdpChannel().VhConfigureAwait();
            else
                await AddTcpDatagramChannel(cancellationToken).VhConfigureAwait();
        }
        catch (Exception ex)
        {
            if (_disposed) return;
            VhLogger.LogError(GeneralEventId.DatagramChannel, ex, "Could not Manage DatagramChannels.");
        }
        finally
        {
            _datagramChannelsSemaphore.Release();
        }
    }

    private async Task AddUdpChannel()
    {
        if (HostTcpEndPoint == null) throw new InvalidOperationException($"{nameof(HostTcpEndPoint)} is not initialized!");
        if (VhUtil.IsNullOrEmpty(ServerSecret)) throw new Exception("ServerSecret has not been set.");
        if (VhUtil.IsNullOrEmpty(_sessionKey)) throw new Exception("Server UdpKey has not been set.");
        if (HostUdpEndPoint == null)
        {
            UseUdpChannel = false;
            throw new Exception("Server does not serve any UDP endpoint.");
        }

        var udpClient = SocketFactory.CreateUdpClient(HostTcpEndPoint.AddressFamily);
        var udpChannel = new UdpChannel(SessionId, _sessionKey, false, ConnectorService.ServerProtocolVersion);
        try
        {
            var udpChannelTransmitter = new ClientUdpChannelTransmitter(udpChannel, udpClient, ServerSecret);
            udpChannel.SetRemote(udpChannelTransmitter, HostUdpEndPoint);
            Tunnel.AddChannel(udpChannel);
        }
        catch
        {
            udpClient.Dispose();
            await udpChannel.DisposeAsync().VhConfigureAwait();
            UseUdpChannel = false;
            throw;
        }
    }

    private async Task ConnectInternal(CancellationToken cancellationToken, bool allowRedirect = true)
    {
        try
        {
            // send hello request
            var clientInfo = new ClientInfo
            {
                ClientId = ClientId,
                ClientVersion = Version.ToString(3),
                ProtocolVersion = ProtocolVersion,
                UserAgent = UserAgent
            };

            var request = new HelloRequest
            {
                RequestId = Guid.NewGuid() + ":client",
                EncryptedClientId = VhUtil.EncryptClientId(clientInfo.ClientId, Token.Secret),
                ClientInfo = clientInfo,
                TokenId = Token.TokenId,
                ServerLocation = _serverFinder.ServerLocation,
                AllowRedirect = allowRedirect,
                IsIpV6Supported = IsIpV6SupportedByClient
            };

            await using var requestResult = await SendRequest<HelloResponse>(request, cancellationToken).VhConfigureAwait();
            var sessionResponse = requestResult.Response;
            if (sessionResponse.ServerProtocolVersion < 4)
                throw new SessionException(SessionErrorCode.UnsupportedServer, "This server is outdated and does not support this client!");

            // initialize the connector
            ConnectorService.Init(
                sessionResponse.ServerProtocolVersion,
                sessionResponse.RequestTimeout,
                sessionResponse.ServerSecret,
                sessionResponse.TcpReuseTimeout);

            // log response
            VhLogger.Instance.LogInformation(GeneralEventId.Session,
                "Hurray! Client has been connected! " +
                $"SessionId: {VhLogger.FormatId(sessionResponse.SessionId)}, " +
                $"ServerVersion: {sessionResponse.ServerVersion}, " +
                $"ServerProtocolVersion: {sessionResponse.ServerProtocolVersion}, " +
                $"ClientIp: {VhLogger.Format(sessionResponse.ClientPublicAddress)}");

            // usage tracker
            if (_allowAnonymousTracker)
            {
                // Anonymous server usage tracker
                if (!string.IsNullOrEmpty(sessionResponse.GaMeasurementId))
                {
                    var ga4Tracking = new Ga4TagTracker()
                    {
                        SessionCount = 1,
                        MeasurementId = sessionResponse.GaMeasurementId,
                        ClientId = ClientId.ToString(),
                        SessionId = SessionId.ToString(),
                        UserAgent = UserAgent,
                        UserProperties = new Dictionary<string, object> { { "client_version", Version.ToString(3) } }
                    };

                    _ = ga4Tracking.Track(new Ga4TagEvent { EventName = TrackEventNames.SessionStart });
                }

                // Anonymous app usage tracker
                if (_usageTracker != null)
                {
                    _ = _usageTracker.Track(ClientTrackerBuilder.BuildConnectionAttempt(connected: true, _serverFinder.ServerLocation, isIpV6Supported: IsIpV6SupportedByClient));
                    _clientUsageTracker = new ClientUsageTracker(Stat, _usageTracker);
                }
            }

            // get session id
            SessionId = sessionResponse.SessionId != 0 ? sessionResponse.SessionId : throw new Exception("Invalid SessionId!");
            _sessionKey = sessionResponse.SessionKey;
            _helloTraffic = sessionResponse.AccessUsage?.Traffic ?? new Traffic();
            ServerSecret = sessionResponse.ServerSecret;
            ResponseAccessKey = sessionResponse.AccessKey;
            SessionStatus.SuppressedTo = sessionResponse.SuppressedTo;
            PublicAddress = sessionResponse.ClientPublicAddress;
            ServerVersion = Version.Parse(sessionResponse.ServerVersion);
            IsIpV6SupportedByServer = sessionResponse.IsIpV6Supported;
            Stat.ServerLocationInfo = sessionResponse.ServerLocation != null ? ServerLocationInfo.Parse(sessionResponse.ServerLocation) : null;
            if (sessionResponse.UdpPort > 0)
                HostUdpEndPoint = new IPEndPoint(ConnectorService.EndPointInfo.TcpEndPoint.Address, sessionResponse.UdpPort.Value);

            // PacketCaptureIpRanges
            if (!VhUtil.IsNullOrEmpty(sessionResponse.PacketCaptureIncludeIpRanges))
                PacketCaptureIncludeIpRanges = PacketCaptureIncludeIpRanges.Intersect(sessionResponse.PacketCaptureIncludeIpRanges);

            // IncludeIpRanges
            if (!VhUtil.IsNullOrEmpty(sessionResponse.IncludeIpRanges) && !sessionResponse.IncludeIpRanges.ToOrderedList().IsAll())
                IncludeIpRanges = IncludeIpRanges.Intersect(sessionResponse.IncludeIpRanges);

            // Get IncludeIpRange for clientIp
            if (_ipRangeProvider != null)
            {
                var filterIpRanges = await _ipRangeProvider.GetIncludeIpRanges(sessionResponse.ClientPublicAddress, cancellationToken).VhConfigureAwait();
                if (!VhUtil.IsNullOrEmpty(filterIpRanges))
                {
                    filterIpRanges = filterIpRanges.Union(DnsServers.Select((x => new IpRange(x))));
                    IncludeIpRanges = IncludeIpRanges.Intersect(filterIpRanges);
                }
            }

            // set DNS after setting IpFilters
            VhLogger.Instance.LogInformation("Configuring Client DNS servers... DnsServers: {DnsServers}", string.Join(", ", DnsServers.Select(x => x.ToString())));
            Stat.IsDnsServersAccepted = VhUtil.IsNullOrEmpty(DnsServers) || DnsServers.Any(IsInIpRange); // no servers means accept default
            if (!Stat.IsDnsServersAccepted)
                VhLogger.Instance.LogWarning("Client DNS servers have been ignored because the server does not route them.");

            DnsServers = DnsServers.Where(IsInIpRange).ToArray();
            if (VhUtil.IsNullOrEmpty(DnsServers))
            {
                DnsServers = VhUtil.IsNullOrEmpty(sessionResponse.DnsServers) ? IPAddressUtil.GoogleDnsServers : sessionResponse.DnsServers;
                IncludeIpRanges = IncludeIpRanges.Union(DnsServers.Select(IpRange.FromIpAddress));
            }

            if (VhUtil.IsNullOrEmpty(DnsServers?.Where(IsInIpRange).ToArray())) // make sure there is at least one DNS server
                throw new Exception("Could not specify any DNS server. The server is not configured properly.");

            // report Suppressed
            if (sessionResponse.SuppressedTo == SessionSuppressType.YourSelf)
                VhLogger.Instance.LogWarning("You suppressed a session of yourself!");

            else if (sessionResponse.SuppressedTo == SessionSuppressType.Other)
                VhLogger.Instance.LogWarning("You suppressed a session of another client!");

            // show ad if required
            if (sessionResponse.AdRequirement is not AdRequirement.None)
                await ShowAd(sessionResponse.AdRequirement is AdRequirement.Required, cancellationToken).VhConfigureAwait();

            // Preparing tunnel
            VhLogger.Instance.LogInformation("Configuring Datagram Channels...");
            Tunnel.MaxDatagramChannelCount = sessionResponse.MaxDatagramChannelCount != 0
                ? Tunnel.MaxDatagramChannelCount = Math.Min(_maxDatagramChannelCount, sessionResponse.MaxDatagramChannelCount)
                : _maxDatagramChannelCount;

            // manage datagram channels
            await ManageDatagramChannels(cancellationToken).VhConfigureAwait();

        }
        catch (RedirectHostException ex) when (allowRedirect)
        {
            // todo: init new connector
            ConnectorService.EndPointInfo.TcpEndPoint = await _serverFinder.FindBestServerAsync(ex.RedirectHostEndPoints, cancellationToken);
            await ConnectInternal(cancellationToken, false).VhConfigureAwait();
        }
    }

    private async Task AddTcpDatagramChannel(CancellationToken cancellationToken)
    {
        // Create and send the Request Message
        var request = new TcpDatagramChannelRequest
        {
            RequestId = Guid.NewGuid() + ":client",
            SessionId = SessionId,
            SessionKey = SessionKey
        };

        var requestResult = await SendRequest<SessionResponse>(request, cancellationToken).VhConfigureAwait();
        StreamDatagramChannel? channel = null;
        try
        {
            // find timespan
            var lifespan = !VhUtil.IsInfinite(_maxTcpDatagramLifespan)
                ? TimeSpan.FromSeconds(new Random().Next((int)_minTcpDatagramLifespan.TotalSeconds, (int)_maxTcpDatagramLifespan.TotalSeconds))
                : Timeout.InfiniteTimeSpan;

            // add the new channel
            channel = new StreamDatagramChannel(requestResult.ClientStream, request.RequestId, lifespan);
            Tunnel.AddChannel(channel);
        }
        catch
        {
            if (channel != null) await channel.DisposeAsync().VhConfigureAwait();
            await requestResult.DisposeAsync().VhConfigureAwait();
            throw;
        }
    }

    internal async Task<ConnectorRequestResult<T>> SendRequest<T>(ClientRequest request, CancellationToken cancellationToken)
        where T : SessionResponse
    {
        if (_disposed)
            throw new ObjectDisposedException(VhLogger.FormatType(this));

        try
        {
            // create a connection and send the request 
            var requestResult = await ConnectorService.SendRequest<T>(request, cancellationToken).VhConfigureAwait();

            // set SessionStatus
            if (requestResult.Response.AccessUsage != null)
                SessionStatus.AccessUsage = requestResult.Response.AccessUsage;

            // client is disposed meanwhile
            if (_disposed)
                throw new ObjectDisposedException(VhLogger.FormatType(this));

            _lastConnectionErrorTime = null;
            State = ClientState.Connected;
            return requestResult;
        }
        catch (SessionException ex)
        {
            // set SessionStatus
            if (ex.SessionResponse.AccessUsage != null)
                SessionStatus.AccessUsage = ex.SessionResponse.AccessUsage;

            // SessionException means that the request accepted by server but there is an error for that request
            _lastConnectionErrorTime = null;

            // close session if server has ended the session
            if (ex.SessionResponse.ErrorCode != SessionErrorCode.GeneralError &&
                ex.SessionResponse.ErrorCode != SessionErrorCode.RedirectHost)
                _ = DisposeAsync(ex);

            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            _ = DisposeAsync(ex);
            throw;
        }
        catch (Exception ex)
        {
            if (_disposed)
                throw;

            var now = FastDateTime.Now;
            _lastConnectionErrorTime ??= now;

            // dispose by session timeout and must before pause because SessionTimeout is bigger than ReconnectTimeout
            if (now - _lastConnectionErrorTime.Value > SessionTimeout)
                _ = DisposeAsync(ex);

            // pause after retry limit
            else if (now - _lastConnectionErrorTime.Value > ReconnectTimeout)
            {
                _autoWaitTime = now;
                State = ClientState.Waiting;
                VhLogger.Instance.LogWarning("Client is paused because of too many connection errors.");
            }

            // set connecting state if it could not establish any connection
            else if (State == ClientState.Connected)
                State = ClientState.Connecting;

            throw;
        }
    }

    public async Task UpdateSessionStatus(CancellationToken cancellationToken = default)
    {
        using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, cancellationToken);

        // don't use SendRequest because it can be disposed
        await using var requestResult = await SendRequest<SessionResponse>(
            new SessionStatusRequest()
            {
                RequestId = Guid.NewGuid() + ":client",
                SessionId = SessionId,
                SessionKey = SessionKey
            },
            linkedCancellationTokenSource.Token)
            .VhConfigureAwait();
    }

    private async Task SendByeRequest(CancellationToken cancellationToken)
    {
        try
        {
            // don't use SendRequest because it can be disposed
            await using var requestResult = await ConnectorService.SendRequest<SessionResponse>(
                new ByeRequest
                {
                    RequestId = Guid.NewGuid() + ":client",
                    SessionId = SessionId,
                    SessionKey = SessionKey
                },
                cancellationToken)
                .VhConfigureAwait();
        }
        catch (Exception ex)
        {
            VhLogger.LogError(GeneralEventId.Session, ex, "Could not send the bye request.");
        }
    }

    private async Task ShowAd(bool required, CancellationToken cancellationToken)
    {
        if (SessionId == 0)
            throw new Exception("SessionId is not set.");

        try
        {
            if (_adProvider == null)
                throw new Exception("AppAdService has not been initialized.");

            _isWaitingForAd = true;
            var adData = await _adProvider.ShowAd(SessionId.ToString(), cancellationToken).VhConfigureAwait();
            if (!string.IsNullOrEmpty(adData) && required)
                _ = SendAdReward(adData, cancellationToken);
        }
        catch (LoadAdException ex)
        {
            if (required)
                throw new LoadAdException("Could not load or show the required ad.", ex);

            VhLogger.Instance.LogInformation(ex, "Could not load or show the flexible ad.");
            // ignore exception for flexible ad if load failed
        }
        finally
        {
            _isWaitingForAd = false;
        }

    }

    private async Task SendAdReward(string adData, CancellationToken cancellationToken)
    {
        try
        {
            // request reward from server
            await using var requestResult = await SendRequest<SessionResponse>(
                new AdRewardRequest
                {
                    RequestId = Guid.NewGuid() + ":client",
                    SessionId = SessionId,
                    SessionKey = SessionKey,
                    AdData = adData
                },
                cancellationToken)
                .VhConfigureAwait();
        }
        catch (Exception ex)
        {
            VhLogger.LogError(GeneralEventId.Session, ex, "Could not send the AdReward request.");
            throw new AdException("This server requires a display ad, but AppAdService has not been initialized.");
        }
    }

    private ValueTask DisposeAsync(Exception ex)
    {
        if (_disposed) return default;

        VhLogger.Instance.LogError(GeneralEventId.Session, ex, "Disposing...");

        // set SessionStatus error code if not set yet
        if (SessionStatus.ErrorCode == SessionErrorCode.Ok)
        {

            if (ex is SessionException sessionException)
            {
                SessionStatus.ErrorCode = sessionException.SessionResponse.ErrorCode;
                SessionStatus.Error = new ApiError(sessionException);
                SessionStatus.SuppressedBy = sessionException.SessionResponse.SuppressedBy;
                if (sessionException.SessionResponse.AccessUsage != null) //update AccessUsage if exists
                {
                    SessionStatus.AccessUsage = sessionException.SessionResponse.AccessUsage;
                    SessionStatus.AccessUsage.Traffic = _helloTraffic; // let calculate it on client
                }
            }
            else
            {
                SessionStatus.ErrorCode = SessionErrorCode.GeneralError;
                SessionStatus.Error = new ApiError(ex);
            }
        }

        return DisposeAsync(false);
    }

    public void Dispose()
    {
        _ = DisposeAsync();
    }

    public ValueTask DisposeAsync()
    {
        return DisposeAsync(false);
    }

    private readonly AsyncLock _disposeLock = new();

    public async ValueTask DisposeAsync(bool waitForBye)
    {
        using var lockResult = await _disposeLock.LockAsync().VhConfigureAwait();
        if (_disposed) return;
        _disposed = true;

        // shutdown
        VhLogger.Instance.LogTrace("Shutting down...");

        _cancellationTokenSource.Cancel();
        var wasConnected = State is ClientState.Connecting or ClientState.Connected;
        State = ClientState.Disconnecting;

        // log suppressedBy
        if (SessionStatus.SuppressedBy == SessionSuppressType.YourSelf)
            VhLogger.Instance.LogWarning("You suppressed by a session of yourself!");

        else if (SessionStatus.SuppressedBy == SessionSuppressType.Other)
            VhLogger.Instance.LogWarning("You suppressed a session of another client!");

        // disposing PacketCapture. Must be at end for graceful shutdown
        _packetCapture.Stopped -= PacketCapture_OnStopped;
        _packetCapture.PacketReceivedFromInbound -= PacketCapture_OnPacketReceivedFromInbound;
        if (_autoDisposePacketCapture)
        {
            VhLogger.Instance.LogTrace("Disposing the PacketCapture...");
            _packetCapture.Dispose();
        }

        var finalizeTask = Finalize(wasConnected);
        if (waitForBye)
            await finalizeTask.VhConfigureAwait();
    }

    private async Task Finalize(bool wasConnected)
    {
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
        if (wasConnected && SessionId != 0 && SessionStatus.ErrorCode == SessionErrorCode.Ok)
        {
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

    public class ClientStat
    {
        private readonly VpnHoodClient _client;
        public ConnectorStat ConnectorStat => _client.ConnectorService.Stat;
        public Traffic Speed => _client.Tunnel.Speed;
        public Traffic SessionTraffic => _client.Tunnel.Traffic;
        public Traffic AccountTraffic => _client._helloTraffic + SessionTraffic;
        public int DatagramChannelCount => _client.Tunnel.DatagramChannelCount;
        public bool IsUdpMode => _client.Tunnel.IsUdpMode;
        public bool IsUdpChannelSupported => _client.HostUdpEndPoint != null;
        public bool IsWaitingForAd => _client._isWaitingForAd;
        public bool IsDnsServersAccepted { get; internal set; }
        public ServerLocationInfo? ServerLocationInfo { get; internal set; }

        internal ClientStat(VpnHoodClient vpnHoodClient)
        {
            _client = vpnHoodClient;
        }
    }
}
