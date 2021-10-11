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
using VpnHood.Common.Messaging;
using VpnHood.Common.Logging;
using VpnHood.Tunneling;
using VpnHood.Tunneling.Factory;
using VpnHood.Tunneling.Messaging;
using ProtocolType = PacketDotNet.ProtocolType;
using VpnHood.Common.Net;

namespace VpnHood.Client
{
    public class VpnHoodClient : IDisposable
    {
        private class SendingPackets
        {
            public readonly List<IPPacket> PassthruPackets = new();
            public readonly List<IPPacket> ProxyPackets = new();
            public readonly List<IPPacket> TcpHostPackets = new();
            public readonly List<IPPacket> TunnelPackets = new();

            public void Clear()
            {
                TunnelPackets.Clear();
                PassthruPackets.Clear();
                TcpHostPackets.Clear();
                ProxyPackets.Clear();
            }
        }

        private readonly bool _autoDisposePacketCapture;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly ClientProxyManager _clientProxyManager;
        private readonly Dictionary<IPAddress, bool> _includeIps = new();
        private readonly int _maxDatagramChannelCount;
        private readonly IPacketCapture _packetCapture;
        private readonly SendingPackets _sendingPacket = new();
        private readonly TcpProxyHost _tcpProxyHost;
        private bool _disposed;
        private Timer? _intervalCheckTimer;
        private bool _isManagingDatagramChannels;
        private DateTime? _lastConnectionErrorTime;
        private byte[]? _sessionKey;
        private ClientState _state = ClientState.None;
        private readonly IPAddress? _dnsServerIpV4;
        private readonly IPAddress? _dnsServerIpV6;

        public event EventHandler? StateChanged;
        private int ProtocolVersion { get; }

        public VpnHoodClient(IPacketCapture packetCapture, Guid clientId, Token token, ClientOptions options)
        {
            if (options.TcpProxyLoopbackAddressIpV4 == null) throw new ArgumentNullException(nameof(options.TcpProxyLoopbackAddressIpV4));
            if (options.TcpProxyLoopbackAddressIpV6 == null) throw new ArgumentNullException(nameof(options.TcpProxyLoopbackAddressIpV6));
            SocketFactory = options.SocketFactory ?? throw new ArgumentNullException(nameof(options.SocketFactory));
            DnsServers = options.DnsServers ?? throw new ArgumentNullException(nameof(options.DnsServers));
            _dnsServerIpV4 = DnsServers.FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork);
            _dnsServerIpV6 = DnsServers.FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetworkV6);

            Token = token ?? throw new ArgumentNullException(nameof(token));
            Version = options.Version ?? throw new ArgumentNullException(nameof(Version));
            UserAgent = options.UserAgent ?? throw new ArgumentNullException(nameof(UserAgent));
            _packetCapture = packetCapture ?? throw new ArgumentNullException(nameof(packetCapture));

            ProtocolVersion = 2;
            _autoDisposePacketCapture = options.AutoDisposePacketCapture;
            _maxDatagramChannelCount = options.MaxTcpDatagramChannelCount;
            _clientProxyManager = new ClientProxyManager(packetCapture, options.SocketFactory);
            ClientId = clientId;
            Timeout = options.Timeout;
            ExcludeLocalNetwork = options.ExcludeLocalNetwork;
            UseUdpChannel = options.UseUdpChannel;
            PacketCaptureIncludeIpRanges = options.PacketCaptureIncludeIpRanges;
            IncludeIpRanges = options.IncludeIpRanges != null ? IpRange.Sort(options.IncludeIpRanges) : null;
            Nat = new Nat(true);

            // init packetCapture cancellation
            packetCapture.OnStopped += PacketCapture_OnStopped;
            packetCapture.OnPacketReceivedFromInbound += PacketCapture_OnPacketReceivedFromInbound;

            // create tunnel
            Tunnel = new Tunnel();
            Tunnel.OnPacketReceived += Tunnel_OnPacketReceived;
            Tunnel.OnChannelRemoved += Tunnel_OnChannelRemoved;

            // create proxy host
            _tcpProxyHost = new TcpProxyHost(this, options.TcpProxyLoopbackAddressIpV4, options.TcpProxyLoopbackAddressIpV6);

#if DEBUG
            if (options.ProtocolVersion != 0) ProtocolVersion = options.ProtocolVersion;
            if (Timeout.TotalSeconds > 30) Timeout = TimeSpan.FromSeconds(10);
#endif
            // Configure thread pool size
            // ThreadPool.GetMinThreads(out int workerThreads, out int completionPortThreads);
            // ThreadPool.SetMinThreads(workerThreads * 2, completionPortThreads * 2);
        }

        internal Nat Nat { get; }
        internal Tunnel Tunnel { get; }
        internal SocketFactory SocketFactory { get; }

        public IPAddress? PublicAddress { get; private set; }
        public TimeSpan Timeout { get; set; }
        public Token Token { get; }
        public Guid ClientId { get; }
        public uint SessionId { get; private set; }

        public byte[] SessionKey => _sessionKey ??
                                    throw new InvalidOperationException(
                                        $"{nameof(SessionKey)} has not been initialized!");

        public IPAddress[] DnsServers { get; }
        public SessionStatus SessionStatus { get; private set; } = new();
        public Version Version { get; }
        public bool ExcludeLocalNetwork { get; }
        public long ReceiveSpeed => Tunnel.ReceiveSpeed;
        public long ReceivedByteCount => Tunnel.ReceivedByteCount;
        public long SendSpeed => Tunnel.SendSpeed;
        public long SentByteCount => Tunnel.SentByteCount;
        public bool UseUdpChannel { get; set; }
        public IpRange[]? IncludeIpRanges { get; }
        public IpRange[]? PacketCaptureIncludeIpRanges { get; }
        public string UserAgent { get; }
        public IPEndPoint? HostEndPoint { get; private set; }

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
            Dispose();
        }

        internal async Task AddPassthruTcpStream(TcpClientStream orgTcpClientStream, IPEndPoint hostEndPoint,
            CancellationToken cancellationToken)
        {
            // config Tcp
            orgTcpClientStream.TcpClient.NoDelay = true;
            Util.TcpClient_SetKeepAlive(orgTcpClientStream.TcpClient, true);

            var tcpClient = SocketFactory.CreateTcpClient(hostEndPoint.AddressFamily);
            tcpClient.ReceiveBufferSize = orgTcpClientStream.TcpClient.ReceiveBufferSize;
            tcpClient.SendBufferSize = orgTcpClientStream.TcpClient.SendBufferSize;
            tcpClient.SendTimeout = orgTcpClientStream.TcpClient.SendTimeout;
            tcpClient.NoDelay = true;
            Util.TcpClient_SetKeepAlive(tcpClient, true);

            // connect to host
            _packetCapture.ProtectSocket(tcpClient.Client);
            await Util.RunTask(tcpClient.ConnectAsync(hostEndPoint.Address, hostEndPoint.Port), cancellationToken: cancellationToken);

            // create add add channel
            var bypassChannel =
                new TcpProxyChannel(orgTcpClientStream, new TcpClientStream(tcpClient, tcpClient.GetStream()));
            _clientProxyManager.AddChannel(bypassChannel);
        }

        public async Task Connect()
        {
            _ = VhLogger.Instance.BeginScope("Client");
            if (_disposed) throw new ObjectDisposedException(nameof(VpnHoodClient));

            if (State != ClientState.None)
                throw new Exception("Connection is already in progress!");

            // report config
            ThreadPool.GetMinThreads(out var workerThreads, out var completionPortThreads);
            VhLogger.Instance.LogInformation($"MinWorkerThreads: {workerThreads}, CompletionPortThreads: {completionPortThreads}");

            // Replace dot in version to prevent anonymous make treat it as ip.
            VhLogger.Instance.LogInformation($"Client is connecting. Version: {Version}");

            // Starting
            State = ClientState.Connecting;
            SessionStatus = new SessionStatus();

            // Connect
            try
            {
                // Init hostEndPoint
                HostEndPoint = await Token.ResolveHostEndPointAsync();

                // Establish first connection and create a session
                await ConnectInternal(_cancellationTokenSource.Token);

                // run interval checker
                _intervalCheckTimer = new Timer(IntervalCheck, null, 0, 5000);

                // create Tcp Proxy Host
                VhLogger.Instance.LogTrace($"Starting {VhLogger.FormatTypeName<TcpProxyHost>()}...");
                _tcpProxyHost.Start();

                // Preparing device
                if (!_packetCapture.Started)
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

            // Filter
            List<IpNetwork> includeNetworks = new();
            if (PacketCaptureIncludeIpRanges?.Length > 0)
            {
                if (PacketCaptureIncludeIpRanges.Any(x => x.IsInRange(hostEndPoint.Address)))
                    throw new InvalidOperationException($"ServerIp can not be part of {nameof(PacketCaptureIncludeIpRanges)}! ServerIp: {hostEndPoint.Address}");
                includeNetworks.AddRange(IpNetwork.FromIpRange(PacketCaptureIncludeIpRanges));
            }
            else
            {
                // Calculate exclude networks
                List<IpNetwork> excludeNetworks = new();

                if (ExcludeLocalNetwork)
                {
                    excludeNetworks.AddRange(IpNetwork.LocalNetworksV4);
                    excludeNetworks.AddRange(IpNetwork.LocalNetworksV6); 
                }

                // exclude server if ProtectSocket is not supported to prevent loop back
                if (!_packetCapture.CanProtectSocket)
                    excludeNetworks.Add(new IpNetwork(hostEndPoint.Address));

                // convert excludeNetworks into includeNetworks
                if (excludeNetworks.Count > 0)
                    includeNetworks.AddRange(IpNetwork.Invert(excludeNetworks));
            }

            // Make sure include all root if nothing is included
            // Make sure LoopbackAddress is included

            // IpV4
            includeNetworks.Add(includeNetworks.All(x => !x.IsIpV4)
                ? IpNetwork.AllV4
                : new IpNetwork(_tcpProxyHost.LoopbackAddressIpV4));

            // IpV6
            includeNetworks.Add(includeNetworks.All(x => !x.IsIpV6)
                ? IpNetwork.AllV6
                : new IpNetwork(_tcpProxyHost.LoopbackAddressIpV6));

            VhLogger.Instance.LogInformation($"PacketCapture Include Networks: {string.Join(", ", includeNetworks.Select(x => x.ToString()))}");
            _packetCapture.IncludeNetworks = includeNetworks.ToArray();
        }

        private void Tunnel_OnChannelRemoved(object sender, ChannelEventArgs e)
        {
            if (e.Channel is IDatagramChannel)
                IntervalCheck(null);
        }

        private void IntervalCheck(object? state)
        {
            _ = ManageDatagramChannels(_cancellationTokenSource.Token);
        }

        // WARNING: Performance Critical!
        private void Tunnel_OnPacketReceived(object sender, ChannelPacketReceivedEventArgs e)
        {
            // manually manage DNS reply if DNS does not supported by _packetCapture
            if (!_packetCapture.IsDnsServersSupported)
                foreach (var ipPacket in e.IpPackets)
                    UpdateDnsRequest(ipPacket, false);

            // forward packet to device
            _packetCapture.SendPacketToInbound(e.IpPackets);
        }

        // WARNING: Performance Critical!
        private void PacketCapture_OnPacketReceivedFromInbound(object sender, Device.PacketReceivedEventArgs e)
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
                    foreach (var ipPacket in e.IpPackets)
                    {
                        if (_cancellationTokenSource.IsCancellationRequested) return;
                        var isInRange = IsInIpRange(ipPacket.DestinationAddress);

                        // Check IPv6 control message such as Solicitations
                        if (IsIcmpControlMessage(ipPacket))
                            continue;

                        // DNS packet must go through tunnel
                        if (!_packetCapture.IsDnsServersSupported && UpdateDnsRequest(ipPacket, true))
                        {
                            tunnelPackets.Add(ipPacket);
                        }
                        // passthru packet if IsSendToOutboundSupported is supported
                        else if (!isInRange && _packetCapture.CanSendPacketToOutbound)
                        {
                            passthruPackets.Add(ipPacket);
                        }

                        // ICMP packet must go through tunnel because PingProxy is not supported
                        else if (ipPacket.Protocol is ProtocolType.Icmp or ProtocolType.IcmpV6)
                        {
                            tunnelPackets.Add(ipPacket);
                        }

                        // TCP packets. isInRange is supported by TcpProxyHost
                        else if (ipPacket.Protocol == ProtocolType.Tcp)
                        {
                            tcpHostPackets.Add(ipPacket);
                        }

                        // Udp
                        else if (ipPacket.Protocol == ProtocolType.Udp)
                        {
                            if (isInRange)
                                tunnelPackets.Add(ipPacket);
                            else
                                proxyPackets.Add(ipPacket);
                        }
                    }

                    // send packets
                    if (passthruPackets.Count > 0) _packetCapture.SendPacketToOutbound(passthruPackets.ToArray());
                    if (proxyPackets.Count > 0) _clientProxyManager.SendPacket(proxyPackets);
                    if (tunnelPackets.Count > 0) Tunnel.SendPacket(tunnelPackets.ToArray());
                    if (tcpHostPackets.Count > 0) _packetCapture.SendPacketToInbound(_tcpProxyHost.ProcessOutgoingPacket(tcpHostPackets.ToArray()));
                }
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogError($"{VhLogger.FormatTypeName(this)}: Error in processing packet! Error: {ex}");
            }
        }

        private bool IsIcmpControlMessage(IPPacket ipPacket)
        {
            // IPv4
            if (ipPacket.Version == IPVersion.IPv4 && ipPacket.Protocol == ProtocolType.Icmp)
            {
                var icmpPacket = ipPacket.Extract<IcmpV4Packet>();
                if (icmpPacket.TypeCode == IcmpV4TypeCode.EchoRequest)
                    return false;
                return true; // drop all other Icmp but echo
            }

            // IPv6
            if (ipPacket.Version == IPVersion.IPv6 && ipPacket.Protocol == ProtocolType.IcmpV6)
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

            // check the cache
            if (_includeIps.TryGetValue(ipAddress, out var isInRange))
                return isInRange;

            // check tcp-loopback
            if (ipAddress.Equals(_tcpProxyHost.LoopbackAddressIpV4) ||
                ipAddress.Equals(_tcpProxyHost.LoopbackAddressIpV6))
                return true;

            // check include
            isInRange = IpRange.IsInRangeFast(IncludeIpRanges, ipAddress);

            // cache the result
            // we really don't need to keep that much ip cache for client
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

            //if (ipPacket?.Extract<UdpPacket>().DestinationPort == 53)
            //{
            //    Console.WriteLine($"outgoing: {outgoing}, passthru");
            //    _packetCapture.SendPacketToOutbound(ipPacket);
            //    return true;
            //}
            //else if (ipPacket?.Extract<UdpPacket>().SourcePort == 53)
            //{
            //    Console.BackgroundColor = ConsoleColor.Red; //todo
            //    Console.WriteLine($"outgoing: {outgoing}");
            //    Console.WriteLine($"{ipPacket}");
            //    Console.ResetColor();
            //}


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
            // check is in progress
            lock (this)
            {
                if (_isManagingDatagramChannels)
                    return;
                _isManagingDatagramChannels = true;
            }

            try
            {
                // make sure only one UdpChannel exists for DatagramChannels if UseUdpChannel is on
                if (UseUdpChannel)
                {
                    // check current channels
                    if (Tunnel.DatagramChannels.Length == 1 && Tunnel.DatagramChannels[0] is UdpChannel)
                        return;

                    // remove all other datagram channel
                    foreach (var channel in Tunnel.DatagramChannels)
                        Tunnel.RemoveChannel(channel);

                    // request udpChannel
                    using var tcpStream = await GetSslConnectionToServer(GeneralEventId.DatagramChannel, cancellationToken);
                    var response = await SendRequest<UdpChannelResponse>(tcpStream.Stream, RequestCode.UdpChannel,
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
                    foreach (var channel in Tunnel.DatagramChannels.Where(x => x is UdpChannel))
                        Tunnel.RemoveChannel(channel);

                    // make sure there is enough DatagramChannel
                    var curDatagramChannelCount = Tunnel.DatagramChannels.Length;
                    if (curDatagramChannelCount >= Tunnel.MaxDatagramChannelCount)
                        return;

                    // creating DatagramChannels
                    List<Task> tasks = new();
                    for (var i = curDatagramChannelCount; i < Tunnel.MaxDatagramChannelCount; i++)
                        tasks.Add(AddTcpDatagramChannel(cancellationToken));

                    await Task.WhenAll(tasks)
                        .ContinueWith(x =>
                        {
                            if (x.IsFaulted)
                                VhLogger.Instance.LogError(
                                    $"Couldn't add a {VhLogger.FormatTypeName<TcpDatagramChannel>()}!", x.Exception);
                            _isManagingDatagramChannels = false;
                        }, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogError(ex.Message);
            }
            finally
            {
                _isManagingDatagramChannels = false;
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
                $"Creating {VhLogger.FormatTypeName<UdpChannel>()}... ServerEp: {udpEndPoint}");

            var udpClient = new UdpClient();
            if (_packetCapture.CanProtectSocket)
                _packetCapture.ProtectSocket(udpClient.Client);
            udpClient.Connect(udpEndPoint);
            var udpChannel = new UdpChannel(true, udpClient, SessionId, udpKey);
            Tunnel.AddChannel(udpChannel);
        }

        internal async Task<TcpClientStream> GetSslConnectionToServer(EventId eventId,
            CancellationToken cancellationToken)
        {
            if (HostEndPoint == null)
                throw new InvalidOperationException($"{nameof(HostEndPoint)} is not initialized!");
            var tcpClient = SocketFactory.CreateTcpClient(HostEndPoint.AddressFamily);
            tcpClient.Client.NoDelay = true;

            try
            {
                // create tcpConnection
                if (_packetCapture.CanProtectSocket)
                    _packetCapture.ProtectSocket(tcpClient.Client);

                // Client.Timeout does not affect in ConnectAsync
                VhLogger.Instance.LogTrace(eventId, $"Connecting to Server: {VhLogger.Format(HostEndPoint)}...");
                await Util.RunTask(tcpClient.ConnectAsync(HostEndPoint.Address, HostEndPoint.Port), Timeout, cancellationToken);

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
                lock (_disposingLock)
                {
                    if (State == ClientState.Connected && !_disposed)
                        State = ClientState.Connecting;
                }

                // dispose by session timeout
                _lastConnectionErrorTime ??= DateTime.Now;
                if (DateTime.Now - _lastConnectionErrorTime.Value > Timeout)
                    Dispose(ex);

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
            var tcpClientStream = await GetSslConnectionToServer(GeneralEventId.Hello, cancellationToken);
            ClientInfo clientInfo = new()
            {
                ClientId = ClientId,
                ClientVersion = Version.ToString(3),
                ProtocolVersion = ProtocolVersion,
                UserAgent = UserAgent
            };

            // Create the hello Message
            var request = new HelloRequest(Token.TokenId, clientInfo,
                Util.EncryptClientId(clientInfo.ClientId, Token.Secret));
            {
                UseUdpChannel = UseUdpChannel;
            }

            // send the request
            HelloResponse response;
            try
            {
                response = await SendRequest<HelloResponse>(tcpClientStream.Stream, RequestCode.Hello, request, cancellationToken);
                if (response.ServerProtocolVersion < 2)
                    throw new SessionException(SessionErrorCode.UnsupportedServer, "This server is outdated and not supported by this client!");
            }
            catch (RedirectHostException ex) when (!redirecting)
            {
                HostEndPoint = ex.RedirectHostEndPoint;
                await ConnectInternal(cancellationToken, true);
                return;
            }

            // get session id
            SessionId = response.SessionId != 0 ? response.SessionId : throw new Exception("Invalid SessionId!");
            _sessionKey = response.SessionKey;
            SessionStatus.SuppressedTo = response.SuppressedTo;
            PublicAddress = response.ClientPublicAddress;

            // Preparing tunnel
            Tunnel.MaxDatagramChannelCount = response.MaxDatagramChannelCount != 0
                ? Tunnel.MaxDatagramChannelCount = Math.Min(_maxDatagramChannelCount, response.MaxDatagramChannelCount)
                : _maxDatagramChannelCount;

            // report Suppressed
            if (response.SuppressedTo == SessionSuppressType.YourSelf)
                VhLogger.Instance.LogWarning("You suppressed a session of yourself!");
            else if (response.SuppressedTo == SessionSuppressType.Other)
                VhLogger.Instance.LogWarning("You suppressed a session of another client!");

            // add the udp channel
            if (UseUdpChannel && response.UdpPort != 0 && response.UdpKey != null)
                AddUdpChannel(response.UdpPort, response.UdpKey);

            await ManageDatagramChannels(cancellationToken);

            // done
            VhLogger.Instance.LogInformation(GeneralEventId.Hello,
                $"Hurray! Client has connected! SessionId: {VhLogger.FormatSessionId(response.SessionId)}, ServerVersion: {response.ServerVersion}");
        }

        private async Task<TcpDatagramChannel> AddTcpDatagramChannel(CancellationToken cancellationToken)
        {
            var tcpClientStream = await GetSslConnectionToServer(GeneralEventId.DatagramChannel, cancellationToken);
            return await AddTcpDatagramChannel(tcpClientStream, cancellationToken);
        }

        private async Task<TcpDatagramChannel> AddTcpDatagramChannel(TcpClientStream tcpClientStream,
            CancellationToken cancellationToken)
        {
            // Create the Request Message
            var request = new TcpDatagramChannelRequest(SessionId, SessionKey);

            // SendRequest
            await SendRequest<ResponseBase>(tcpClientStream.Stream, RequestCode.TcpDatagramChannel, request,
                cancellationToken);

            // add the new channel
            var channel = new TcpDatagramChannel(tcpClientStream);
            Tunnel.AddChannel(channel);
            return channel;
        }

        internal async Task<T> SendRequest<T>(Stream stream, RequestCode requestCode, object request,
            CancellationToken cancellationToken) where T : ResponseBase
        {
            try
            {
                // log this request
                var eventId = requestCode switch
                {
                    RequestCode.Hello => GeneralEventId.Hello,
                    RequestCode.TcpDatagramChannel => GeneralEventId.DatagramChannel,
                    RequestCode.TcpProxyChannel => GeneralEventId.StreamChannel,
                    _ => GeneralEventId.Tcp
                };
                VhLogger.Instance.LogTrace(eventId, $"Sending a request... RequestCode: {requestCode}");

                // building request
                await using var mem = new MemoryStream();
                mem.WriteByte(1);
                mem.WriteByte((byte)requestCode);
                await StreamUtil.WriteJsonAsync(mem, request, cancellationToken);

                // send the request
                await stream.WriteAsync(mem.ToArray(), cancellationToken);

                // Reading the response
                var response = await StreamUtil.ReadJsonAsync<T>(stream, cancellationToken);
                VhLogger.Instance.LogTrace(eventId, $"Received a response... ErrorCode: {response.ErrorCode}");

                // set SessionStatus
                if (response.AccessUsage != null)
                    SessionStatus.AccessUsage = response.AccessUsage;

                // client is disposed mean while
                if (_disposed)
                    throw new ObjectDisposedException(VhLogger.FormatTypeName(this));

                if (response.ErrorCode == SessionErrorCode.RedirectHost)
                    throw new RedirectHostException(response);

                if (response.ErrorCode != SessionErrorCode.Ok)
                    throw new SessionException(response);

                _lastConnectionErrorTime = null;
                State = ClientState.Connected;
                return response;
            }
            catch (Exception ex)
            {
                var sessionResponse = ex is SessionException sessionException ? sessionException.SessionResponse : null;
                if (sessionResponse?.ErrorCode is SessionErrorCode.GeneralError or SessionErrorCode.RedirectHost)
                {
                    // GeneralError and RedirectHost mean that the request accepted by server but there is an error to the request
                    _lastConnectionErrorTime = null;
                }
                else
                {
                    // dispose by session timeout
                    _lastConnectionErrorTime ??= DateTime.Now;
                    if (ex is SessionException || DateTime.Now - _lastConnectionErrorTime.Value > Timeout)
                        Dispose(ex);
                }

                throw;
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
                    SessionStatus.ErrorCode = sessionException.SessionResponse.ErrorCode;
                    SessionStatus.ErrorMessage = sessionException.SessionResponse.ErrorMessage;
                    SessionStatus.SuppressedBy = sessionException.SessionResponse.SuppressedBy;
                    if (sessionException.SessionResponse.AccessUsage != null) //update AccessUsage if exists
                        SessionStatus.AccessUsage = sessionException.SessionResponse.AccessUsage;
                }
                else
                {
                    SessionStatus.ErrorCode = SessionErrorCode.GeneralError;
                    SessionStatus.ErrorMessage = ex.Message;
                }
            }

            Dispose();
        }

        private readonly object _disposingLock = new();
        public void Dispose()
        {
            lock (_disposingLock)
            {
                if (_disposed) return;
                _disposed = true;
            }

            if (State == ClientState.None) return;

            VhLogger.Instance.LogTrace("Disconnecting...");
            if (State == ClientState.Connecting || State == ClientState.Connected)
                State = ClientState.Disconnecting;
            _cancellationTokenSource.Cancel();

            // log suppressedBy
            if (SessionStatus.SuppressedBy == SessionSuppressType.YourSelf)
                VhLogger.Instance.LogWarning("You suppressed by a session of yourself!");
            else if (SessionStatus.SuppressedBy == SessionSuppressType.Other)
                VhLogger.Instance.LogWarning("You suppressed a session of another client!");

            // shutdown
            VhLogger.Instance.LogTrace("Shutting down...");
            _intervalCheckTimer?.Dispose();

            VhLogger.Instance.LogTrace($"Disposing {VhLogger.FormatTypeName<TcpProxyHost>()}...");
            _tcpProxyHost.Dispose();

            VhLogger.Instance.LogTrace($"Disposing {VhLogger.FormatTypeName<Tunnel>()}...");
            Tunnel.OnPacketReceived -= Tunnel_OnPacketReceived;
            Tunnel.OnChannelRemoved -= Tunnel_OnChannelRemoved;
            Tunnel.Dispose();

            VhLogger.Instance.LogTrace($"Disposing {VhLogger.FormatTypeName<ProxyManager>()}...");
            _clientProxyManager.Dispose();

            // dispose NAT
            VhLogger.Instance.LogTrace($"Disposing {VhLogger.FormatTypeName(Nat)}...");
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
}