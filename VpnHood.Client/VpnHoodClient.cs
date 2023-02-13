using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Client.Device;
using VpnHood.Client.Exceptions;
using VpnHood.Common;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;
using VpnHood.Common.Utils;
using VpnHood.Tunneling;
using VpnHood.Tunneling.Factory;
using VpnHood.Tunneling.Messaging;
using PacketReceivedEventArgs = VpnHood.Client.Device.PacketReceivedEventArgs;
using ProtocolType = PacketDotNet.ProtocolType;

namespace VpnHood.Client;

public class VpnHoodClient : IDisposable, IAsyncDisposable
{
    private class SendingPackets
    {
        public readonly List<IPPacket> PassthruPackets = new();
        public readonly List<IPPacket> ProxyPackets = new();
        public readonly List<IPPacket> TcpHostPackets = new();
        public readonly List<IPPacket> TunnelPackets = new();
        public readonly List<IPPacket> DroppedPackets = new();

        public void Clear()
        {
            TunnelPackets.Clear();
            PassthruPackets.Clear();
            TcpHostPackets.Clear();
            ProxyPackets.Clear();
            DroppedPackets.Clear();
        }
    }

    private readonly bool _autoDisposePacketCapture;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly CancellationToken _cancellationToken;
    private readonly ClientProxyManager _proxyManager;
    private readonly Dictionary<IPAddress, bool> _includeIps = new();
    private readonly int _maxDatagramChannelCount;
    private readonly IPacketCapture _packetCapture;
    private readonly SendingPackets _sendingPacket = new();
    private readonly TcpProxyHost _tcpProxyHost;
    private bool _disposed;
    private readonly SemaphoreSlim _datagramChannelsSemaphore = new(1, 1);
    private DateTime? _lastConnectionErrorTime;
    private byte[]? _sessionKey;
    private ClientState _state = ClientState.None;
    private readonly IPAddress? _dnsServerIpV4;
    private readonly IPAddress? _dnsServerIpV6;
    private readonly IIpFilter? _ipFilter;
    private readonly TimeSpan _minTcpDatagramLifespan;
    private readonly TimeSpan _maxTcpDatagramLifespan;
    private bool _udpChannelAdded;
    private DateTime _lastReceivedPacketTime = DateTime.MinValue;
    private int ProtocolVersion { get; }
    private bool IsTcpDatagramLifespanSupported => ServerVersion?.Build >= 345; //will be deprecated

    internal Nat Nat { get; }
    internal Tunnel Tunnel { get; }
    internal SocketFactory SocketFactory { get; }

    public Version? ServerVersion { get; private set; }
    public event EventHandler? StateChanged;
    public IPAddress? PublicAddress { get; private set; }
    public bool IsIpV6Supported { get; private set; }
    public TimeSpan SessionTimeout { get; set; }
    public TimeSpan TcpTimeout { get; set; }
    public Token Token { get; }
    public Guid ClientId { get; }
    public uint SessionId { get; private set; }
    public IPAddress[] DnsServers { get; }
    public SessionStatus SessionStatus { get; private set; } = new();
    public Version Version { get; }
    public bool IncludeLocalNetwork { get; }
    public long ReceiveSpeed => Tunnel.ReceiveSpeed;
    public long ReceivedByteCount => Tunnel.ReceivedByteCount;
    public long SendSpeed => Tunnel.SendSpeed;
    public long SentByteCount => Tunnel.SentByteCount;
    public bool UseUdpChannel { get; set; }
    public IpRange[]? IncludeIpRanges { get; private set; }
    public IpRange[] PacketCaptureIncludeIpRanges { get; private set; }
    public string UserAgent { get; }
    public IPEndPoint? HostEndPoint { get; private set; }
    public int DatagramChannelsCount => Tunnel.DatagramChannels.Length;

    public VpnHoodClient(IPacketCapture packetCapture, Guid clientId, Token token, ClientOptions options)
    {
        if (options.TcpProxyCatcherAddressIpV4 == null)
            throw new ArgumentNullException(nameof(options.TcpProxyCatcherAddressIpV4));

        if (options.TcpProxyCatcherAddressIpV6 == null)
            throw new ArgumentNullException(nameof(options.TcpProxyCatcherAddressIpV6));

        if (!Util.IsInfinite(_maxTcpDatagramLifespan) && _maxTcpDatagramLifespan < _minTcpDatagramLifespan)
            throw new ArgumentNullException(nameof(options.MaxTcpDatagramTimespan), $"{nameof(options.MaxTcpDatagramTimespan)} must be bigger or equal than {nameof(options.MinTcpDatagramTimespan)}.");

        SocketFactory = options.SocketFactory ?? throw new ArgumentNullException(nameof(options.SocketFactory));
        DnsServers = options.DnsServers ?? throw new ArgumentNullException(nameof(options.DnsServers));
        _dnsServerIpV4 = DnsServers.FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork);
        _dnsServerIpV6 = DnsServers.FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetworkV6);
        _minTcpDatagramLifespan = options.MinTcpDatagramTimespan;
        _maxTcpDatagramLifespan = options.MaxTcpDatagramTimespan;

        Token = token ?? throw new ArgumentNullException(nameof(token));
        Version = options.Version ?? throw new ArgumentNullException(nameof(Version));
        UserAgent = options.UserAgent ?? throw new ArgumentNullException(nameof(UserAgent));
        _packetCapture = packetCapture ?? throw new ArgumentNullException(nameof(packetCapture));

        ProtocolVersion = 2;
        _autoDisposePacketCapture = options.AutoDisposePacketCapture;
        _maxDatagramChannelCount = options.MaxDatagramChannelCount;
        _proxyManager = new ClientProxyManager(packetCapture, options.SocketFactory, new ProxyManagerOptions());
        _ipFilter = options.IpFilter;
        ClientId = clientId;
        SessionTimeout = options.SessionTimeout;
        TcpTimeout = options.TcpTimeout;
        IncludeLocalNetwork = options.IncludeLocalNetwork;
        UseUdpChannel = options.UseUdpChannel;
        PacketCaptureIncludeIpRanges = options.PacketCaptureIncludeIpRanges;
        Nat = new Nat(true);

        // init packetCapture cancellation
        packetCapture.OnStopped += PacketCapture_OnStopped;
        packetCapture.OnPacketReceivedFromInbound += PacketCapture_OnPacketReceivedFromInbound;

        // create tunnel
        Tunnel = new Tunnel();
        Tunnel.OnPacketReceived += Tunnel_OnPacketReceived;
        Tunnel.OnChannelRemoved += Tunnel_OnChannelRemoved;

        // create proxy host
        _tcpProxyHost = new TcpProxyHost(this, options.TcpProxyCatcherAddressIpV4, options.TcpProxyCatcherAddressIpV6);

        // Create simple disposable objects
        _cancellationTokenSource = new CancellationTokenSource();
        _cancellationToken = _cancellationTokenSource.Token;

#if DEBUG
        if (options.ProtocolVersion != 0) ProtocolVersion = options.ProtocolVersion;
#endif
    }

    public byte[] SessionKey => _sessionKey ??
                                throw new InvalidOperationException($"{nameof(SessionKey)} has not been initialized!");

    public ClientState State
    {
        get => _state;
        private set
        {
            if (_state == value) return;
            _state = value; //must set before raising the event; 
            VhLogger.Instance.LogInformation($"Client is {State}");
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void PacketCapture_OnStopped(object sender, EventArgs e)
    {
        VhLogger.Instance.LogTrace("Device has been stopped!");
        _ = DisposeAsync();
    }

    internal async Task AddPassthruTcpStream(TcpClientStream orgTcpClientStream, IPEndPoint hostEndPoint,
        CancellationToken cancellationToken)
    {
        var tcpClient = SocketFactory.CreateTcpClient(hostEndPoint.AddressFamily);
        tcpClient.ReceiveBufferSize = orgTcpClientStream.TcpClient.ReceiveBufferSize;
        tcpClient.SendBufferSize = orgTcpClientStream.TcpClient.SendBufferSize;
        tcpClient.SendTimeout = orgTcpClientStream.TcpClient.SendTimeout;
        SocketFactory.SetKeepAlive(tcpClient.Client, true);

        // connect to host
        _packetCapture.ProtectSocket(tcpClient.Client);
        await Util.RunTask(tcpClient.ConnectAsync(hostEndPoint.Address, hostEndPoint.Port), cancellationToken: cancellationToken);

        // create add add channel
        var bypassChannel = new TcpProxyChannel(orgTcpClientStream, new TcpClientStream(tcpClient, tcpClient.GetStream()), TunnelUtil.TcpTimeout);
        try { _proxyManager.AddChannel(bypassChannel); }
        catch { bypassChannel.Dispose(); throw; }
    }

    public async Task Connect()
    {
        using var scope = VhLogger.Instance.BeginScope("Client");
        if (_disposed) throw new ObjectDisposedException(nameof(VpnHoodClient));

        if (State != ClientState.None)
            throw new Exception("Connection is already in progress!");

        // report config
        ThreadPool.GetMinThreads(out var workerThreads, out var completionPortThreads);
        VhLogger.Instance.LogInformation($"UseUdpChannel: {UseUdpChannel}, MinWorkerThreads: {workerThreads}, CompletionPortThreads: {completionPortThreads}");

        // Replace dot in version to prevent anonymous make treat it as ip.
        VhLogger.Instance.LogInformation($"Client Version: {Version}, ClientId: {VhLogger.FormatId(ClientId)}");

        // Starting
        State = ClientState.Connecting;
        SessionStatus = new SessionStatus();

        // Connect
        try
        {
            // Init hostEndPoint
            HostEndPoint = await Token.ResolveHostEndPointAsync();

            // Establish first connection and create a session
            await ConnectInternal(_cancellationToken);

            // create Tcp Proxy Host
            VhLogger.Instance.LogTrace($"Starting {VhLogger.FormatType(_tcpProxyHost)}...");
            _tcpProxyHost.Start();

            // Preparing device;
            if (!_packetCapture.Started) //make sure it is not a shared packet capture
            {
                ConfigPacketFilter(HostEndPoint);
                _packetCapture.StartCapture();
            }

            State = ClientState.Connected;
        }
        catch (Exception ex)
        {
            Dispose(ex);
            throw;
        }
    }

    private void ConfigPacketFilter(IPEndPoint hostEndPoint)
    {
        // DnsServer
        if (_packetCapture.IsDnsServersSupported)
            _packetCapture.DnsServers = DnsServers;

        // Built-in IpV6 support
        if (_packetCapture.IsAddIpV6AddressSupported)
            _packetCapture.AddIpV6Address = true; //lets block ipV6 if not supported

        // Start with user PacketCaptureIncludeIpRanges
        var includeIpRanges = (IEnumerable<IpRange>)PacketCaptureIncludeIpRanges;

        // exclude server if ProtectSocket is not supported to prevent loop
        if (!_packetCapture.CanProtectSocket)
            includeIpRanges = includeIpRanges.Exclude(new[] { new IpRange(hostEndPoint.Address) });

        // exclude local networks
        if (!IncludeLocalNetwork)
            includeIpRanges = includeIpRanges.Exclude(IpNetwork.LocalNetworks.ToIpRanges());

        // Make sure CatcherAddress is included
        includeIpRanges = includeIpRanges.Concat(new[]
        {
            new IpRange(_tcpProxyHost.CatcherAddressIpV4),
            new IpRange(_tcpProxyHost.CatcherAddressIpV6)
        });

        _packetCapture.IncludeNetworks = includeIpRanges.Sort().ToIpNetworks().ToArray(); //sort and unify
        VhLogger.Instance.LogInformation($"PacketCapture Include Networks: {string.Join(", ", _packetCapture.IncludeNetworks.Select(x => x.ToString()))}");
    }

    private void Tunnel_OnChannelRemoved(object sender, ChannelEventArgs e)
    {
        // device is sleep. Don't wake it up
        if (!Util.IsInfinite(_maxTcpDatagramLifespan) && FastDateTime.Now - _lastReceivedPacketTime > _maxTcpDatagramLifespan)
            return;

        if (e.Channel is IDatagramChannel)
            _ = ManageDatagramChannels(_cancellationToken);
    }

    // WARNING: Performance Critical!
    private void Tunnel_OnPacketReceived(object sender, ChannelPacketReceivedEventArgs e)
    {
        // manually manage DNS reply if DNS does not supported by _packetCapture
        if (!_packetCapture.IsDnsServersSupported)
            foreach (var ipPacket in e.IpPackets)
                UpdateDnsRequest(ipPacket, false);

        _packetCapture.SendPacketToInbound(e.IpPackets);
        _lastReceivedPacketTime = FastDateTime.Now;
    }

    // WARNING: Performance Critical!
    private void PacketCapture_OnPacketReceivedFromInbound(object sender, PacketReceivedEventArgs e)
    {
        if (_disposed)
            return;

        try
        {
            lock (_sendingPacket) // this method should not be called in multi-thread, if so we need to allocate the list per call
            {
                _sendingPacket.Clear(); // prevent reallocation in this intensive event
                var tunnelPackets = _sendingPacket.TunnelPackets;
                var tcpHostPackets = _sendingPacket.TcpHostPackets;
                var passthruPackets = _sendingPacket.PassthruPackets;
                var proxyPackets = _sendingPacket.ProxyPackets;
                var droppedPackets = _sendingPacket.DroppedPackets;
                foreach (var ipPacket in e.IpPackets)
                {
                    if (_disposed) return;
                    var isIpV6 = ipPacket.DestinationAddress.AddressFamily == AddressFamily.InterNetworkV6;

                    // DNS packet must go through tunnel even if it is not in range
                    if (!_packetCapture.IsDnsServersSupported && UpdateDnsRequest(ipPacket, true))
                    {
                        // Drop IPv6 if not support
                        if (isIpV6 && !IsIpV6Supported)
                            droppedPackets.Add(ipPacket);
                        else
                            tunnelPackets.Add(ipPacket);
                    }

                    else if (_packetCapture.CanSendPacketToOutbound)
                    {
                        if (!IsInIpRange(ipPacket.DestinationAddress))
                            passthruPackets.Add(ipPacket);

                        // Drop IPv6 if not support
                        else if (isIpV6 && !IsIpV6Supported)
                            droppedPackets.Add(ipPacket);

                        // Check IPv6 control message such as Solicitations
                        else if (IsIcmpControlMessage(ipPacket))
                            droppedPackets.Add(ipPacket);

                        else if (ipPacket.Protocol == ProtocolType.Tcp)
                            tcpHostPackets.Add(ipPacket);

                        else if (ipPacket.Protocol is ProtocolType.Icmp or ProtocolType.IcmpV6 or ProtocolType.Udp)
                            tunnelPackets.Add(ipPacket);

                        else
                            droppedPackets.Add(ipPacket);

                    }
                    else
                    {
                        // Drop IPv6 if not support
                        if (isIpV6 && !IsIpV6Supported)
                            droppedPackets.Add(ipPacket);

                        // Check IPv6 control message such as Solicitations
                        else if (IsIcmpControlMessage(ipPacket))
                            droppedPackets.Add(ipPacket);

                        else if (ipPacket.Protocol == ProtocolType.Tcp)
                            tcpHostPackets.Add(ipPacket);

                        // ICMP packet must go through tunnel because PingProxy does not supported protect socket
                        else if (ipPacket.Protocol is ProtocolType.Icmp or ProtocolType.IcmpV6)
                            tunnelPackets.Add(ipPacket);

                        // Udp
                        else if (ipPacket.Protocol == ProtocolType.Udp)
                        {
                            if (IsInIpRange(ipPacket.DestinationAddress))
                                tunnelPackets.Add(ipPacket);
                            else
                                proxyPackets.Add(ipPacket);
                        }

                        else
                            droppedPackets.Add(ipPacket);

                    }
                }

                // send packets
                if (tunnelPackets.Count > 0) _ = ManageDatagramChannels(_cancellationToken);
                if (tunnelPackets.Count > 0) Tunnel.SendPackets(tunnelPackets).Wait(_cancellationToken);
                if (passthruPackets.Count > 0) _packetCapture.SendPacketToOutbound(passthruPackets.ToArray());
                if (proxyPackets.Count > 0) _proxyManager.SendPackets(proxyPackets).Wait(_cancellationToken);
                if (tcpHostPackets.Count > 0) _packetCapture.SendPacketToInbound(_tcpProxyHost.ProcessOutgoingPacket(tcpHostPackets));
            }
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(ex, "Could not process packet the capture packets.");
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
        if (Util.IsNullOrEmpty(IncludeIpRanges))
            return true;

        // check tcp-loopback
        if (ipAddress.Equals(_tcpProxyHost.CatcherAddressIpV4) ||
            ipAddress.Equals(_tcpProxyHost.CatcherAddressIpV6))
            return true;

        // check the cache
        if (_includeIps.TryGetValue(ipAddress, out var isInRange))
            return isInRange;

        // check include
        isInRange = IpRange.IsInSortedRanges(IncludeIpRanges, ipAddress);

        // cache the result
        // we really don't need to keep that much ips in the cache
        if (_includeIps.Count > 0xFFFF)
        {
            VhLogger.Instance.LogInformation("Clearing IP filter cache!");
            _includeIps.Clear();
        }

        _includeIps.Add(ipAddress, isInRange);
        return isInRange;
    }

    private bool UpdateDnsRequest(IPPacket ipPacket, bool outgoing)
    {
        if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));
        if (ipPacket.Protocol != ProtocolType.Udp) return false;

        // find dns server
        var dnsServer = ipPacket.Version == IPVersion.IPv4 ? _dnsServerIpV4 : _dnsServerIpV6;
        if (dnsServer == null)
        {
            VhLogger.Instance.LogWarning($"There is no DNS server for {ipPacket.DestinationAddress.AddressFamily}");
            return false;
        }

        // manage DNS outgoing packet if requested DNS is not VPN DNS
        if (outgoing && !ipPacket.DestinationAddress.Equals(dnsServer))
        {
            var udpPacket = PacketUtil.ExtractUdp(ipPacket);
            if (udpPacket.DestinationPort == 53) //53 is DNS port
            {
                VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.Dns,
                    $"DNS request from {VhLogger.Format(ipPacket.SourceAddress)}:{udpPacket.SourcePort} to {VhLogger.Format(ipPacket.DestinationAddress)}, Map to: {VhLogger.Format(dnsServer)}");
                udpPacket.SourcePort = Nat.GetOrAdd(ipPacket).NatId;
                ipPacket.DestinationAddress = dnsServer;
                PacketUtil.UpdateIpPacket(ipPacket);
                return true;
            }
        }

        // manage DNS incoming packet from VPN DNS
        else if (!outgoing && ipPacket.SourceAddress.Equals(dnsServer))
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

    /// <returns>true if managing is in progress</returns>
    private async Task ManageDatagramChannels(CancellationToken cancellationToken)
    {
        if (!await _datagramChannelsSemaphore.WaitAsync(0, cancellationToken))
            return;

        try
        {
            // make sure only one UdpChannel exists for DatagramChannels if UseUdpChannel is on
            if (UseUdpChannel)
            {
                // check current channels
                // ReSharper disable once MergeIntoPattern
                if (Tunnel.DatagramChannels.Length == 1 && Tunnel.DatagramChannels[0] is UdpChannel)
                    return;

                // remove all other datagram channel
                foreach (var channel in Tunnel.DatagramChannels)
                    Tunnel.RemoveChannel(channel);

                // request udpChannel
                using var tcpStream = await GetTlsConnectionToServer(GeneralEventId.DatagramChannel, cancellationToken);
                var response = await SendRequest<UdpChannelSessionResponse>(tcpStream.Stream, RequestCode.UdpChannel,
                    new UdpChannelRequest(SessionId, SessionKey), cancellationToken);

                if (response.UdpPort != 0)
                    AddUdpChannel(response.UdpPort, response.UdpKey);
                else
                    UseUdpChannel = false;
            }

            // don't use else; UseUdpChannel may be changed if server does not assign the channel
            if (!UseUdpChannel)
            {
                // remove UDP datagram channels
                if (_udpChannelAdded)
                    foreach (var channel in Tunnel.DatagramChannels.Where(x => x is UdpChannel))
                        Tunnel.RemoveChannel(channel);
                _udpChannelAdded = false;

                // make sure there is enough DatagramChannel
                var curDatagramChannelCount = Tunnel.DatagramChannels.Length;
                if (curDatagramChannelCount >= Tunnel.MaxDatagramChannelCount)
                    return;

                // creating DatagramChannels
                var tasks = new List<Task>();
                for (var i = curDatagramChannelCount; i < Tunnel.MaxDatagramChannelCount; i++)
                    tasks.Add(AddTcpDatagramChannel(cancellationToken));

                await Task.WhenAll(tasks)
                    .ContinueWith(x =>
                    {
                        if (x.IsFaulted)
                            VhLogger.Instance.LogError(GeneralEventId.DatagramChannel, x.Exception, "Could not add a TcpDatagramChannel.");
                    }, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            if (_disposed) return;
            VhLogger.Instance.LogError(GeneralEventId.DatagramChannel, ex, "Could not Manage DatagramChannels.");
        }
        finally
        {
            _datagramChannelsSemaphore.Release();
        }
    }

    private void AddUdpChannel(int udpPort, byte[] udpKey)
    {
        if (HostEndPoint == null)
            throw new InvalidOperationException($"{nameof(HostEndPoint)} is not initialized!");

        if (udpPort == 0) throw new ArgumentException(nameof(udpPort));
        if (udpKey == null || udpKey.Length == 0) throw new ArgumentNullException(nameof(udpKey));

        var udpEndPoint = new IPEndPoint(HostEndPoint.Address, udpPort);
        VhLogger.Instance.LogInformation(GeneralEventId.DatagramChannel,
            "Creating a UdpChannel... ServerEp: {ServerEp}", VhLogger.Format(udpEndPoint));

        var udpClient = SocketFactory.CreateUdpClient(HostEndPoint.AddressFamily);
        if (_packetCapture.CanProtectSocket)
            _packetCapture.ProtectSocket(udpClient.Client);
        udpClient.Connect(udpEndPoint);

        // add channel
        var udpChannel = new UdpChannel(true, udpClient, SessionId, udpKey);
        try
        {
            _udpChannelAdded = true; // let have it before add channel to make sure it will be removed if any exception occur
            Tunnel.AddChannel(udpChannel);
        }
        catch
        {
            udpChannel.Dispose(); throw;
        }
    }

    internal async Task<TcpClientStream> GetTlsConnectionToServer(EventId eventId, CancellationToken cancellationToken)
    {
        if (HostEndPoint == null)
            throw new InvalidOperationException($"{nameof(HostEndPoint)} is not initialized!");
        var tcpClient = SocketFactory.CreateTcpClient(HostEndPoint.AddressFamily);

        try
        {
            // create tcpConnection
            if (_packetCapture.CanProtectSocket)
                _packetCapture.ProtectSocket(tcpClient.Client);

            // Client.SessionTimeout does not affect in ConnectAsync
            VhLogger.Instance.LogTrace(eventId, $"Connecting to Server: {VhLogger.Format(HostEndPoint)}...");
            await Util.RunTask(tcpClient.ConnectAsync(HostEndPoint.Address, HostEndPoint.Port), TcpTimeout, cancellationToken);

            // start TLS
            var stream = new SslStream(tcpClient.GetStream(), true, UserCertificateValidationCallback);

            // Establish a TLS connection
            VhLogger.Instance.LogTrace(eventId, $"TLS Authenticating. HostName: {VhLogger.FormatDns(Token.HostName)}...");
            var sslProtocol = Environment.OSVersion.Platform == PlatformID.Win32NT &&
                              Environment.OSVersion.Version.Major < 10
                ? SslProtocols.Tls12 // windows 7
                : SslProtocols.None; //auto

            await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = Token.HostName,
                EnabledSslProtocols = sslProtocol
            }, cancellationToken);

            return new TcpClientStream(tcpClient, stream);
        }
        catch (Exception ex)
        {
            // clean up TcpClient
            tcpClient.Dispose();
            if (!_disposed && State == ClientState.Connected)
                State = ClientState.Connecting;

            // dispose by session timeout
            _lastConnectionErrorTime ??= FastDateTime.Now;
            if (FastDateTime.Now - _lastConnectionErrorTime.Value > SessionTimeout)
                Dispose(ex);

            // convert MaintenanceException
            if (SessionStatus.ErrorCode == SessionErrorCode.Maintenance)
                throw new MaintenanceException();

            // Bobble up
            throw;
        }
    }

    private bool UserCertificateValidationCallback(object sender, X509Certificate certificate, X509Chain chain,
        SslPolicyErrors sslPolicyErrors)
    {
        // check maintenance certificate
        var parts = certificate.Subject.Split(",");
        if (parts.Any(x => x.Trim().Equals("OU=MT", StringComparison.OrdinalIgnoreCase)))
        {
            SessionStatus.ErrorCode = SessionErrorCode.Maintenance;
            return false;
        }

        return sslPolicyErrors == SslPolicyErrors.None ||
               Token.CertificateHash.SequenceEqual(certificate.GetCertHash());
    }

    private async Task ConnectInternal(CancellationToken cancellationToken, bool redirecting = false)
    {
        var tcpClientStream = await GetTlsConnectionToServer(GeneralEventId.Session, cancellationToken);
        ClientInfo clientInfo = new()
        {
            ClientId = ClientId,
            ClientVersion = Version.ToString(3),
            ProtocolVersion = ProtocolVersion,
            UserAgent = UserAgent
        };

        // Create the hello Message
        var request = new HelloRequest(Token.TokenId, clientInfo,
            Util.EncryptClientId(clientInfo.ClientId, Token.Secret))
        {
            UseUdpChannel = UseUdpChannel
        };

        // send the request
        HelloSessionResponse sessionResponse;
        try
        {
            sessionResponse = await SendRequest<HelloSessionResponse>(tcpClientStream.Stream, RequestCode.Hello, request, cancellationToken);
            if (sessionResponse.ServerProtocolVersion < 2)
                throw new SessionException(SessionErrorCode.UnsupportedServer, "This server is outdated and does not support this client!");
        }
        catch (RedirectHostException ex) when (!redirecting)
        {
            HostEndPoint = ex.RedirectHostEndPoint;
            await ConnectInternal(cancellationToken, true);
            return;
        }

        // get session id
        SessionId = sessionResponse.SessionId != 0 ? sessionResponse.SessionId : throw new Exception("Invalid SessionId!");
        _sessionKey = sessionResponse.SessionKey;
        SessionStatus.SuppressedTo = sessionResponse.SuppressedTo;
        PublicAddress = sessionResponse.ClientPublicAddress;
        IsIpV6Supported = sessionResponse.IsIpV6Supported;
        ServerVersion = Version.Parse(sessionResponse.ServerVersion);

        // PacketCaptureIpRanges
        if (!Util.IsNullOrEmpty(sessionResponse.PacketCaptureIncludeIpRanges))
            PacketCaptureIncludeIpRanges = PacketCaptureIncludeIpRanges.Intersect(sessionResponse.PacketCaptureIncludeIpRanges).ToArray();

        // IncludeIpRanges
        if (!Util.IsNullOrEmpty(sessionResponse.IncludeIpRanges) && !sessionResponse.IncludeIpRanges.ToIpNetworks().IsAll())
        {
            IncludeIpRanges ??= IpNetwork.All.ToIpRanges().ToArray();
            IncludeIpRanges = IncludeIpRanges.Intersect(sessionResponse.IncludeIpRanges).ToArray();
        }

        // Get IncludeIpRange for clientIp
        var filterIpRanges = _ipFilter != null ? await _ipFilter.GetIncludeIpRanges(sessionResponse.ClientPublicAddress) : null;
        if (!Util.IsNullOrEmpty(filterIpRanges))
        {
            IncludeIpRanges ??= IpNetwork.All.ToIpRanges().ToArray();
            IncludeIpRanges = IncludeIpRanges.Intersect(filterIpRanges).ToArray();
        }

        // Preparing tunnel
        Tunnel.MaxDatagramChannelCount = sessionResponse.MaxDatagramChannelCount != 0
            ? Tunnel.MaxDatagramChannelCount = Math.Min(_maxDatagramChannelCount, sessionResponse.MaxDatagramChannelCount)
            : _maxDatagramChannelCount;

        // report Suppressed
        if (sessionResponse.SuppressedTo == SessionSuppressType.YourSelf)
            VhLogger.Instance.LogWarning("You suppressed a session of yourself!");
        else if (sessionResponse.SuppressedTo == SessionSuppressType.Other)
            VhLogger.Instance.LogWarning("You suppressed a session of another client!");

        // add the udp channel
        if (UseUdpChannel && sessionResponse.UdpPort != 0 && sessionResponse.UdpKey != null)
            AddUdpChannel(sessionResponse.UdpPort, sessionResponse.UdpKey);

        _ = ManageDatagramChannels(cancellationToken);

        // done
        VhLogger.Instance.LogInformation(GeneralEventId.Session,
            "Hurray! Client has connected! " +
            $"SessionId: {VhLogger.FormatId(sessionResponse.SessionId)}, " +
            $"ServerVersion: {sessionResponse.ServerVersion}, " +
            $"ClientIp: {VhLogger.Format(sessionResponse.ClientPublicAddress)}");
    }

    private async Task<TcpDatagramChannel> AddTcpDatagramChannel(CancellationToken cancellationToken)
    {
        var tcpClientStream = await GetTlsConnectionToServer(GeneralEventId.DatagramChannel, cancellationToken);

        // Create the Request Message
        var request = new TcpDatagramChannelRequest(SessionId, SessionKey);

        // SendRequest
        await SendRequest<SessionResponseBase>(tcpClientStream.Stream, RequestCode.TcpDatagramChannel,
            request, cancellationToken);

        // find timespan
        var lifespan = !Util.IsInfinite(_maxTcpDatagramLifespan) && IsTcpDatagramLifespanSupported
            ? TimeSpan.FromSeconds(new Random().Next((int)_minTcpDatagramLifespan.TotalSeconds, (int)_maxTcpDatagramLifespan.TotalSeconds))
            : Timeout.InfiniteTimeSpan;

        // add the new channel
        var channel = new TcpDatagramChannel(tcpClientStream, lifespan);
        try { Tunnel.AddChannel(channel); }
        catch { channel.Dispose(); throw; }

        return channel;
    }

    internal async Task<T> SendRequest<T>(Stream stream, RequestCode requestCode, object request,
        CancellationToken cancellationToken) where T : SessionResponseBase
    {
        try
        {
            // log this request
            var eventId = requestCode switch
            {
                RequestCode.Hello => GeneralEventId.Session,
                RequestCode.TcpDatagramChannel => GeneralEventId.DatagramChannel,
                RequestCode.TcpProxyChannel => GeneralEventId.TcpProxyChannel,
                _ => GeneralEventId.Tcp
            };
            VhLogger.Instance.LogTrace(eventId, $"Sending a request... RequestCode: {requestCode}.");

            // building request
            await using var mem = new MemoryStream();
            mem.WriteByte(1);
            mem.WriteByte((byte)requestCode);
            await StreamUtil.WriteJsonAsync(mem, request, cancellationToken);

            // send the request
            await stream.WriteAsync(mem.ToArray(), cancellationToken);

            // Reading the response
            var response = await StreamUtil.ReadJsonAsync<T>(stream, cancellationToken);

            // extract response
            VhLogger.Instance.LogTrace(eventId, $"Received a response... ErrorCode: {response.ErrorCode}.");

            // set SessionStatus
            if (response.AccessUsage != null)
                SessionStatus.AccessUsage = response.AccessUsage;

            // client is disposed mean while
            if (_disposed)
                throw new ObjectDisposedException(VhLogger.FormatType(this));

            if (response.ErrorCode == SessionErrorCode.RedirectHost) throw new RedirectHostException(response);
            if (response.ErrorCode == SessionErrorCode.Maintenance) throw new MaintenanceException();
            if (response.ErrorCode != SessionErrorCode.Ok) throw new SessionException(response);

            _lastConnectionErrorTime = null;
            State = ClientState.Connected;
            return response;
        }
        catch (SessionException ex) when (ex.SessionResponseBase.ErrorCode is SessionErrorCode.GeneralError or SessionErrorCode.RedirectHost)
        {
            // GeneralError and RedirectHost mean that the request accepted by server but there is an error for that request
            _lastConnectionErrorTime = null;
            throw;
        }
        catch (Exception ex)
        {
            // dispose by session timeout or known exception
            _lastConnectionErrorTime ??= FastDateTime.Now;
            if (ex is SessionException or UnauthorizedAccessException || FastDateTime.Now - _lastConnectionErrorTime.Value > SessionTimeout)
                Dispose(ex);
            throw;
        }
    }

    private async Task SendByeRequest(TimeSpan timeout)
    {
        try
        {
            // create cancellation token
            using var cancellationTokenSource = new CancellationTokenSource(timeout);
            var cancellationToken = cancellationTokenSource.Token;

            using var tcpClientStream = await GetTlsConnectionToServer(GeneralEventId.Session, cancellationToken);

            // building request
            await using var mem = new MemoryStream();
            mem.WriteByte(1);
            mem.WriteByte((byte)RequestCode.Bye);
            await StreamUtil.WriteJsonAsync(mem, new RequestBase(SessionId, SessionKey), cancellationToken);

            // send the request
            await tcpClientStream.Stream.WriteAsync(mem.ToArray(), cancellationToken);
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogInformation(GeneralEventId.Session, $"Could not send the {RequestCode.Bye} request! Error: {ex.Message}");
        }
    }

    private void Dispose(Exception ex)
    {
        if (_disposed) return;

        VhLogger.Instance.LogError($"Disposing. Error! {ex}");

        // set SessionStatus error code if not set yet
        if (SessionStatus.ErrorCode == SessionErrorCode.Ok)
        {

            if (ex is SessionException sessionException)
            {
                SessionStatus.ErrorCode = sessionException.SessionResponseBase.ErrorCode;
                SessionStatus.ErrorMessage = sessionException.SessionResponseBase.ErrorMessage;
                SessionStatus.SuppressedBy = sessionException.SessionResponseBase.SuppressedBy;
                if (sessionException.SessionResponseBase.AccessUsage != null) //update AccessUsage if exists
                    SessionStatus.AccessUsage = sessionException.SessionResponseBase.AccessUsage;
            }
            else
            {
                SessionStatus.ErrorCode = SessionErrorCode.GeneralError;
                SessionStatus.ErrorMessage = ex.Message;
            }
        }

        _ = DisposeAsync();
    }

    public void Dispose()
    {
        Task.Run(async () => await DisposeAsync(), CancellationToken.None).GetAwaiter().GetResult();
    }

    private readonly AsyncLock _disposeLock = new();
    public async ValueTask DisposeAsync()
    {
        // wait for dispose completion
        var disposeTimeout = TimeSpan.FromSeconds(10);
        using var disposeLock = await _disposeLock.LockAsync(disposeTimeout, CancellationToken.None);
        if (!disposeLock.Succeeded)
            return;

        // return if already disposed
        if (_disposed) return;
        _disposed = true;

        if (State == ClientState.None)
            return;

        VhLogger.Instance.LogTrace("Disconnecting...");
        if (State is ClientState.Connecting or ClientState.Connected)
        {
            State = ClientState.Disconnecting;
            if (SessionId != 0)
            {
                VhLogger.Instance.LogTrace("Sending the Bye request!");
                await SendByeRequest(disposeTimeout);
            }
        }
        _cancellationTokenSource.Cancel();

        // log suppressedBy
        if (SessionStatus.SuppressedBy == SessionSuppressType.YourSelf)
            VhLogger.Instance.LogWarning("You suppressed by a session of yourself!");

        else if (SessionStatus.SuppressedBy == SessionSuppressType.Other)
            VhLogger.Instance.LogWarning("You suppressed a session of another client!");

        // shutdown
        VhLogger.Instance.LogTrace("Shutting down...");
        VhLogger.Instance.LogTrace($"Disposing {VhLogger.FormatType(_tcpProxyHost)}...");
        _tcpProxyHost.Dispose();

        VhLogger.Instance.LogTrace($"Disposing {VhLogger.FormatType(Tunnel)}...");
        Tunnel.OnPacketReceived -= Tunnel_OnPacketReceived;
        Tunnel.OnChannelRemoved -= Tunnel_OnChannelRemoved;
        Tunnel.Dispose();

        VhLogger.Instance.LogTrace($"Disposing {VhLogger.FormatType(_proxyManager)}...");
        _proxyManager.Dispose();

        // dispose NAT
        VhLogger.Instance.LogTrace($"Disposing {VhLogger.FormatType(Nat)}...");
        Nat.Dispose();

        // close PacketCapture
        _packetCapture.OnStopped -= PacketCapture_OnStopped;
        _packetCapture.OnPacketReceivedFromInbound -= PacketCapture_OnPacketReceivedFromInbound;
        if (_autoDisposePacketCapture)
        {
            VhLogger.Instance.LogTrace("Disposing the PacketCapture...");
            _packetCapture.Dispose();
        }

        State = ClientState.Disposed;
        VhLogger.Instance.LogInformation("Bye Bye!");
    }
}