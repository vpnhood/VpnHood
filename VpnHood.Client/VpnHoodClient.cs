using Microsoft.Extensions.Logging;
using PacketDotNet;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using VpnHood.Common;
using VpnHood.Tunneling;
using VpnHood.Logging;
using VpnHood.Tunneling.Messages;
using VpnHood.Client.Device;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using VpnHood.Tunneling.Factory;

namespace VpnHood.Client
{
    public class VpnHoodClient : IDisposable
    {
        class ClientProxyManager : ProxyManager
        {
            private readonly VpnHoodClient _client;
            public ClientProxyManager(VpnHoodClient client) => _client = client;
            protected override Ping CreatePing() //PacketCapture can not protect Ping so PingProxy does not work
                => throw new NotSupportedException($"{nameof(CreatePing)} is not supported by {nameof(ClientProxyManager)}!");
            protected override UdpClient CreateUdpClient()
            {
                var udpClient = _client.SocketFactory.CreateUdpClient();
                _client._packetCapture.ProtectSocket(udpClient.Client);
                return udpClient;
            }
            protected override void SendReceivedPacket(IPPacket ipPacket)
                => _client._packetCapture.SendPacketToInbound(ipPacket);
        }

        class SendingPackets
        {
            public readonly List<IPPacket> TunnelPackets = new();
            public readonly List<IPPacket> PassthruPackets = new();
            public readonly List<IPPacket> TcpHostPackets = new();
            public readonly List<IPPacket> ProxyPackets = new();
            public void Clear()
            {
                TunnelPackets.Clear();
                PassthruPackets.Clear();
                TcpHostPackets.Clear();
                ProxyPackets.Clear();
            }
        }

        private readonly IPacketCapture _packetCapture;
        private readonly bool _leavePacketCaptureOpen;
        private TcpProxyHost _tcpProxyHost;
        private readonly ClientProxyManager _clientProxyManager;
        private CancellationTokenSource _cancellationTokenSource;
        private readonly int _maxDatagramChannelCount;
        private bool _disposed;
        private bool _isManagaingDatagramChannels;
        private DateTime? _lastConnectionErrorTime = null;
        private Timer _intervalCheckTimer;
        private readonly Dictionary<IPAddress, bool> _includeIps = new();
        private readonly SendingPackets _sendingPacket = new();
        private readonly IpRange[] _packetCaptureExcludeIpRanges;

        internal Nat Nat { get; }
        internal Tunnel Tunnel { get; private set; }
        internal SocketFactory SocketFactory { get; }

        public int Timeout { get; set; }
        public Token Token { get; }
        public Guid ClientId { get; }
        public int SessionId { get; private set; }
        public byte[] SessionKey { get; private set; }
        public string ServerId { get; private set; }
        public bool Connected { get; private set; }
        public IPAddress TcpProxyLoopbackAddress { get; }
        public IPAddress[] DnsServers { get; }
        public event EventHandler StateChanged;
        public SessionStatus SessionStatus { get; private set; } = new SessionStatus();
        public Version Version { get; }
        public bool ExcludeLocalNetwork { get; }
        public long ReceiveSpeed => Tunnel?.ReceiveSpeed ?? 0;
        public long ReceivedByteCount => Tunnel?.ReceivedByteCount ?? 0;
        public long SendSpeed => Tunnel?.SendSpeed ?? 0;
        public long SentByteCount => Tunnel?.SentByteCount ?? 0;
        public bool UseUdpChannel { get; set; }
        public IpRange[] IncludeIpRanges { get; set; }
        public IpRange[] ExcludeIpRanges { get; set; }

        public VpnHoodClient(IPacketCapture packetCapture, Guid clientId, Token token, ClientOptions options)
        {
            _packetCapture = packetCapture ?? throw new ArgumentNullException(nameof(packetCapture));
            _leavePacketCaptureOpen = options.LeavePacketCaptureOpen;
            _maxDatagramChannelCount = options.MaxTcpDatagramChannelCount;
            _clientProxyManager = new ClientProxyManager(this);
            _packetCaptureExcludeIpRanges = options.PacketCaptureExcludeIpRange;
            Token = token ?? throw new ArgumentNullException(nameof(token));
            DnsServers = options.DnsServers ?? throw new ArgumentNullException(nameof(options.DnsServers));
            if (DnsServers.Length == 0) throw new ArgumentException("Atleast one DnsServer must be set!", nameof(options.DnsServers));
            TcpProxyLoopbackAddress = options.TcpProxyLoopbackAddress ?? throw new ArgumentNullException(nameof(options.TcpProxyLoopbackAddress));
            ClientId = clientId;
            Timeout = options.Timeout;
            Version = options.Version;
            ExcludeLocalNetwork = options.ExcludeLocalNetwork;
            UseUdpChannel = options.UseUdpChannel;
            SocketFactory = options.SocketFactory ?? new();
            IncludeIpRanges = options.IncludeIpRanges != null ? IpRange.Sort(options.IncludeIpRanges).ToArray() : null;
            ExcludeIpRanges = options.ExcludeIpRanges != null ? IpRange.Sort(options.ExcludeIpRanges).ToArray() : null;
            Nat = new Nat(true);

            packetCapture.OnStopped += PacketCature_OnStopped;

            // Configure thread pool size
            // ThreadPool.GetMinThreads(out int workerThreads, out int completionPortThreads);
            // ThreadPool.SetMinThreads(workerThreads * 2, completionPortThreads * 2);
        }

        private ClientState _state = ClientState.None;
        public ClientState State
        {
            get => _state;
            private set
            {
                if (_state == value) return;
                _state = value;
                VhLogger.Instance.LogInformation($"Client is {State}");
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void PacketCature_OnStopped(object sender, EventArgs e)
        {
            VhLogger.Instance.LogInformation("Device has been stopped!");
            Dispose();
        }

        private IPEndPoint _serverEndPoint;
        public IPEndPoint ServerTcpEndPoint
        {
            get
            {
                if (_serverEndPoint != null)
                    return _serverEndPoint;

                var random = new Random();
                if (Token.IsValidDns)
                {
                    try
                    {
                        VhLogger.Instance.LogInformation($"Resolving IP from host name: {VhLogger.FormatDns(Token.DnsName)}...");
                        var hostEntry = Dns.GetHostEntry(Token.DnsName);
                        if (hostEntry.AddressList.Length > 0)
                        {
                            var index = random.Next(0, hostEntry.AddressList.Length);
                            var ip = hostEntry.AddressList[index];
                            var serverEndPoint = Util.ParseIpEndPoint(Token.ServerEndPoint);

                            VhLogger.Instance.LogInformation($"{hostEntry.AddressList.Length} IP founds. {ip}:{serverEndPoint.Port} has been Selected!");
                            _serverEndPoint = new IPEndPoint(ip, serverEndPoint.Port);
                            return _serverEndPoint;
                        }
                    }
                    catch { };
                }
                else
                {
                    VhLogger.Instance.LogInformation($"Extracting host from the token. Host: {VhLogger.FormatDns(Token.ServerEndPoint)}");
                    var index = random.Next(0, Token.ServerEndPoints.Length);
                    _serverEndPoint = Util.ParseIpEndPoint(Token.ServerEndPoints[index]);
                    return _serverEndPoint;
                }

                throw new Exception("Could not resolve Server Address!");
            }
        }

        internal async Task AddPassthruTcpStream(TcpClientStream orgTcpClientStream, IPEndPoint hostEndPoint, CancellationToken cancellationToken)
        {
            // config Tcp
            orgTcpClientStream.TcpClient.NoDelay = true;
            Util.TcpClient_SetKeepAlive(orgTcpClientStream.TcpClient, true);

            var tcpClient = SocketFactory.CreateTcpClient();
            tcpClient.ReceiveBufferSize = orgTcpClientStream.TcpClient.ReceiveBufferSize;
            tcpClient.SendBufferSize = orgTcpClientStream.TcpClient.SendBufferSize;
            tcpClient.SendTimeout = orgTcpClientStream.TcpClient.SendTimeout;
            tcpClient.NoDelay = true;
            Util.TcpClient_SetKeepAlive(tcpClient, true);

            // connect to host
            _packetCapture.ProtectSocket(tcpClient.Client);
            await Util.TcpClient_ConnectAsync(tcpClient, hostEndPoint.Address, hostEndPoint.Port, 0, cancellationToken);

            // create add add channel
            var bypassChannel = new TcpProxyChannel(orgTcpClientStream, new TcpClientStream(tcpClient, tcpClient.GetStream()));
            _clientProxyManager.AddChannel(bypassChannel);
        }

        public async Task Connect()
        {
            _ = VhLogger.Instance.BeginScope("Client");
            if (_disposed) throw new ObjectDisposedException(nameof(VpnHoodClient));

            if (State != ClientState.None)
                throw new Exception("Connection is already in progress!");

            // report config
            ThreadPool.GetMinThreads(out int workerThreads, out int completionPortThreads);
            VhLogger.Instance.LogInformation($"MinWorkerThreads: {workerThreads}, CompletionPortThreads: {completionPortThreads}");

            // Replace dot in version to prevent anonymous make treat it as ip.
            VhLogger.Instance.LogInformation($"Client is connecting. Version: {Version}");

            // Starting
            State = ClientState.Connecting;
            SessionStatus = new SessionStatus();
            _cancellationTokenSource = new CancellationTokenSource();

            // Connect
            try
            {
                // Establish first connection and create a session
                await Task.Run(() => ConnectInternal(_cancellationTokenSource.Token));

                // run interval checker
                _intervalCheckTimer = new Timer(IntervalCheck, null, 0, 5000);

                // create Tcp Proxy Host
                VhLogger.Instance.LogTrace($"Creating {VhLogger.FormatTypeName<TcpProxyHost>()}...");
                _tcpProxyHost = new TcpProxyHost(this, TcpProxyLoopbackAddress);
                _ = _tcpProxyHost.StartListening();

                // Preparing device
                if (!_packetCapture.Started)
                {
                    ConfigPacketFilter();
                    _packetCapture.StartCapture();
                }

                _packetCapture.OnPacketReceivedFromInbound += PacketCapture_OnPacketReceivedFromInbound;
                State = ClientState.Connected;
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogError($"Error! {ex}");
                Dispose(ex);
                throw;
            }
        }

        private void ConfigPacketFilter()
        {
            // DnsServer
            if (_packetCapture.IsDnsServersSupported)
                _packetCapture.DnsServers = DnsServers;

            // clear include networks
            _packetCapture.IncludeNetworks = new IpNetwork[] { IpNetwork.Parse("0.0.0.0/0") };

            // Calculate exclude networks
            List<IpNetwork> excludeNetworks = new();

            // Add driver exclude networks
            if (_packetCaptureExcludeIpRanges?.Length > 0)
                excludeNetworks.AddRange(IpNetwork.FromIpRange(_packetCaptureExcludeIpRanges));

            if (ExcludeLocalNetwork)
                excludeNetworks.AddRange(IpNetwork.LocalNetworks);

            // exclude server if ProtectSocket is not supported to prevent loop back
            if (!_packetCapture.CanProtectSocket)
                excludeNetworks.Add(new IpNetwork(ServerTcpEndPoint.Address));

            // Exclude serverEp
            if (excludeNetworks.Count > 0)
            {
                VhLogger.Instance.LogInformation($"Excluding Networks: {string.Join(", ", excludeNetworks.Select(x => $"{x.Prefix}/{x.PrefixLength}"))}");
                List<IpNetwork> includeNetworks = new(IpNetwork.Invert(excludeNetworks));
                includeNetworks.Add(new IpNetwork(TcpProxyLoopbackAddress, 32)); //make sure TcpProxyLoop back is added to routes
                _packetCapture.IncludeNetworks = includeNetworks.ToArray();
            }
        }

        private void Tunnel_OnChannelRemoved(object sender, ChannelEventArgs e)
        {
            if (e.Channel is IDatagramChannel)
                IntervalCheck(null);
        }

        private void IntervalCheck(object state)
        {
            _ = ManageDatagramChannels(_cancellationTokenSource.Token);
        }

        // WARNING: Performance Critical!
        private void Tunnel_OnPacketReceived(object sender, ChannelPacketReceivedEventArgs e)
        {
            // manually manage DNS reply if DNS does not supported by _packetCapture
            if (!_packetCapture.IsDnsServersSupported)
            {
                foreach (var ipPacket in e.IpPackets)
                    UpdateDnsRequest(ipPacket, false);
            }

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
                lock (_sendingPacket) // this method should not be called in multithread, if so we need to allocate the list per call
                {
                    _sendingPacket.Clear(); // prevent reallocation in this intensive event
                    var tunnelPackets = _sendingPacket.TunnelPackets;
                    var tcpHostPackets = _sendingPacket.TcpHostPackets;
                    var passthruPackets = _sendingPacket.PassthruPackets;
                    var proxyPackets = _sendingPacket.ProxyPackets;
                    foreach (var ipPacket in e.IpPackets)
                    {
                        if (_cancellationTokenSource.IsCancellationRequested) return;
                        if (ipPacket.Version != IPVersion.IPv4)
                            continue;

                        var isInRange = IsInIpRange(ipPacket.DestinationAddress);

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
                        else if (ipPacket.Protocol == PacketDotNet.ProtocolType.Icmp)
                        {
                            tunnelPackets.Add(ipPacket);
                        }

                        // TCP packets. isInRange is supported by TcpProxyHost
                        else if (ipPacket.Protocol == PacketDotNet.ProtocolType.Tcp)
                        {
                            tcpHostPackets.Add(ipPacket);
                        }

                        // Udp
                        else if (ipPacket.Protocol == PacketDotNet.ProtocolType.Udp)
                        {
                            if (isInRange)
                                tunnelPackets.Add(ipPacket);
                            else
                                proxyPackets.Add(ipPacket);
                        }
                    }

                    // send packets
                    if (passthruPackets.Count > 0) _packetCapture.SendPacketToOutbound(passthruPackets);
                    if (proxyPackets.Count > 0) _clientProxyManager.SendPacket(proxyPackets);
                    if (tunnelPackets.Count > 0) Tunnel.SendPacket(tunnelPackets);
                    if (tcpHostPackets.Count > 0) _packetCapture.SendPacketToInbound(_tcpProxyHost.ProcessOutgoingPacket(tcpHostPackets));
                }
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogError($"{VhLogger.FormatTypeName(this)}: Error in processing packet! Error: {ex}");
            }
        }

        public bool IsInIpRange(IPAddress ipAddress)
        {
            // all IPs are included if there is no filter
            if (IncludeIpRanges == null && ExcludeIpRanges == null)
                return true;

            // check the cache
            if (_includeIps.TryGetValue(ipAddress, out bool result))
                return result;

            // check tcp-loopback
            if (ipAddress.Equals(TcpProxyLoopbackAddress))
                return true;

            // check include
            result =
                (IncludeIpRanges == null || IpRange.IsInRange(IncludeIpRanges, ipAddress)) &&
                (ExcludeIpRanges == null || !IpRange.IsInRange(ExcludeIpRanges, ipAddress));

            // cache the resukt
            // we really don't need to keep that much ip cache for client
            if (_includeIps.Count > 0xFFFF)
            {
                VhLogger.Instance.LogInformation("Clearing IP filter cache!");
                _includeIps.Clear();
            }
            _includeIps.Add(ipAddress, result);
            return result;
        }

        private bool UpdateDnsRequest(IPPacket ipPacket, bool outgoing)
        {
            if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));
            if (ipPacket.Protocol != PacketDotNet.ProtocolType.Udp) return false;
            var dnsServer = DnsServers[0]; //use first DNS

            // manage DNS outgoing packet if requested DNS is not VPN DNS
            if (outgoing && !ipPacket.DestinationAddress.Equals(dnsServer))
            {
                var udpPacket = PacketUtil.ExtractUdp(ipPacket);
                if (udpPacket.DestinationPort == 53) //53 is DNS port
                {
                    VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.Dns, $"DNS request from {VhLogger.Format(ipPacket.SourceAddress)}:{udpPacket.SourcePort} to {VhLogger.Format(ipPacket.DestinationAddress)}, Map to: {VhLogger.Format(dnsServer)}");
                    udpPacket.SourcePort = Nat.GetOrAdd(ipPacket).NatId;
                    ipPacket.DestinationAddress = dnsServer;
                    PacketUtil.UpdateIpPacket(ipPacket);
                    return true;
                }
            }

            // manage DNS incomming packet from VPN DNS
            else if (!outgoing && ipPacket.SourceAddress.Equals(dnsServer))
            {
                var udpPacket = PacketUtil.ExtractUdp(ipPacket);
                var natItem = (NatItemEx)Nat.Resolve(PacketDotNet.ProtocolType.Udp, udpPacket.DestinationPort);
                if (natItem != null)
                {
                    VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.Dns, $"DNS reply to {VhLogger.Format(natItem.DestinationAddress)}:{natItem.DestinationPort}");
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
                if (_isManagaingDatagramChannels)
                    return;
                _isManagaingDatagramChannels = true;
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
                        new UdpChannelRequest { SessionId = SessionId, SessionKey = SessionKey },
                        cancellationToken);

                    if (response.UdpPort != 0)
                        AddUdpChannel(response.UdpPort, response.UdpKey);
                    else
                        UseUdpChannel = false;
                }

                // don't use else; UseUdpChannel may be changed if server doet not assign the channel
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

                    await Task.WhenAll(tasks).ContinueWith(x =>
                    {
                        if (x.IsFaulted)
                            VhLogger.Instance.LogError($"Couldn't add a {VhLogger.FormatTypeName<TcpDatagramChannel>()}!", x.Exception);
                        _isManagaingDatagramChannels = false;
                    });
                }
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogError(ex.Message);
            }
            finally
            {
                _isManagaingDatagramChannels = false;
            }
        }

        private void AddUdpChannel(int udpPort, byte[] udpKey)
        {
            if (udpPort == 0) throw new ArgumentException(nameof(udpPort));
            if (udpKey == null || udpKey.Length == 0) throw new ArgumentNullException(nameof(udpKey));

            var udpEndPoint = new IPEndPoint(ServerTcpEndPoint.Address, udpPort);
            VhLogger.Instance.LogInformation(GeneralEventId.DatagramChannel, $"Creating {VhLogger.FormatTypeName<UdpChannel>()}... ServerEp: {udpEndPoint}");

            var udpClient = new UdpClient();
            if (_packetCapture.CanProtectSocket)
                _packetCapture.ProtectSocket(udpClient.Client);
            udpClient.Connect(udpEndPoint);
            var udpChannel = new UdpChannel(true, udpClient, SessionId, udpKey);
            Tunnel.AddChannel(udpChannel);
        }

        internal async Task<TcpClientStream> GetSslConnectionToServer(EventId eventId, CancellationToken cancellationToken)
        {
            var tcpClient = SocketFactory.CreateTcpClient();
            tcpClient.Client.NoDelay = true;

            try
            {
                // create tcpConnection
                if (_packetCapture.CanProtectSocket)
                    _packetCapture.ProtectSocket(tcpClient.Client);

                // Client.Timeout does not affect in ConnectAsync
                VhLogger.Instance.LogTrace(eventId, $"Connecting to Server: {VhLogger.Format(ServerTcpEndPoint)}...");
                await Util.TcpClient_ConnectAsync(tcpClient, ServerTcpEndPoint.Address, ServerTcpEndPoint.Port, Timeout, cancellationToken);

                // start TLS
                var stream = new SslStream(tcpClient.GetStream(), true, UserCertificateValidationCallback);

                // Establish a TLS connection
                VhLogger.Instance.LogTrace(eventId, $"TLS Authenticating. HostName: {VhLogger.FormatDns(Token.DnsName)}...");
                var sslProtocol = Environment.OSVersion.Platform == PlatformID.Win32NT && Environment.OSVersion.Version.Major < 10
                    ? System.Security.Authentication.SslProtocols.Tls12 // windows 7
                    : System.Security.Authentication.SslProtocols.None; //auto

                await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = Token.DnsName,
                    EnabledSslProtocols = sslProtocol
                }, cancellationToken);

                _lastConnectionErrorTime = null;
                return new TcpClientStream(tcpClient, stream);
            }
            catch (Exception ex)
            {
                // clean up TcpClient
                tcpClient?.Dispose();
                if (State == ClientState.Connected)
                    State = ClientState.Connecting;

                // set _lastConnectionErrorTime
                if (_lastConnectionErrorTime == null)
                    _lastConnectionErrorTime = DateTime.Now;

                // dispose client after long waiting socket error
                if (!_disposed && (DateTime.Now - _lastConnectionErrorTime.Value).TotalMilliseconds > Timeout)
                    Dispose(ex);

                throw;
            }
        }

        private bool UserCertificateValidationCallback(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return sslPolicyErrors == SslPolicyErrors.None ||
                Token.CertificateHash == null ||
                Token.CertificateHash.SequenceEqual(certificate.GetCertHash());
        }

        private async Task ConnectInternal(CancellationToken cancellationToken)
        {
            var tcpClientStream = await GetSslConnectionToServer(GeneralEventId.Hello, cancellationToken);

            // Encrypt ClientId
            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Key = Token.Secret;
            aes.IV = new byte[Token.Secret.Length];
            aes.Padding = PaddingMode.None;
            using var cryptor = aes.CreateEncryptor();
            var encryptedClientId = cryptor.TransformFinalBlock(ClientId.ToByteArray(), 0, ClientId.ToByteArray().Length);

            // Create the hello Message
            var request = new HelloRequest()
            {
                ClientVersion = Version.ToString(3),
                ClientProtocolVersion = 1,
                ClientId = ClientId,
                TokenId = Token.TokenId,
                EncryptedClientId = encryptedClientId,
                UseUdpChannel = UseUdpChannel,
            };

            // send the request
            var response = await SendRequest<HelloResponse>(tcpClientStream.Stream, RequestCode.Hello, request, cancellationToken);
            if (response.SessionId == 0)
                throw new Exception($"Invalid SessionId!");

            // get session id
            SessionId = response.SessionId;
            SessionKey = response.SessionKey;
            ServerId = response.ServerId;
            SessionStatus.SuppressedTo = response.SuppressedTo;

            // Preparing tunnel
            Tunnel = new Tunnel();
            Tunnel.OnPacketReceived += Tunnel_OnPacketReceived;
            Tunnel.OnChannelRemoved += Tunnel_OnChannelRemoved;
            Tunnel.MaxDatagramChannelCount = response.MaxDatagramChannelCount != 0
                ? Tunnel.MaxDatagramChannelCount = Math.Min(_maxDatagramChannelCount, response.MaxDatagramChannelCount)
                : _maxDatagramChannelCount;

            // report Suppressed
            if (response.SuppressedTo == SuppressType.YourSelf)
                VhLogger.Instance.LogWarning($"You suppressed a session of yourself!");
            else if (response.SuppressedTo == SuppressType.Other)
                VhLogger.Instance.LogWarning($"You suppressed a session of another client!");

            // add the udp channel
            if (UseUdpChannel && response.UdpPort != 0)
                AddUdpChannel(response.UdpPort, response.UdpKey);

            await ManageDatagramChannels(cancellationToken);

            // done
            VhLogger.Instance.LogInformation(GeneralEventId.Hello, $"Hurray! Client has connected! SessionId: {VhLogger.FormatSessionId(response.SessionId)}, ServerId: {response.ServerId}, ServerVersion: {response.ServerVersion}");
            Connected = true;
        }

        private async Task<TcpDatagramChannel> AddTcpDatagramChannel(CancellationToken cancellationToken)
        {
            var tcpClientStream = await GetSslConnectionToServer(GeneralEventId.DatagramChannel, cancellationToken);
            return await AddTcpDatagramChannel(tcpClientStream, cancellationToken);
        }

        private async Task<TcpDatagramChannel> AddTcpDatagramChannel(TcpClientStream tcpClientStream, CancellationToken cancellationToken)
        {
            // Create the Request Message
            var request = new TcpDatagramChannelRequest()
            {
                SessionId = SessionId,
                SessionKey = SessionKey
            };

            // SendRequest
            await SendRequest<BaseResponse>(tcpClientStream.Stream, RequestCode.TcpDatagramChannel, request, cancellationToken);

            // add the new channel
            var channel = new TcpDatagramChannel(tcpClientStream);
            Tunnel.AddChannel(channel);
            return channel;
        }

        internal async Task<T> SendRequest<T>(Stream stream, RequestCode requestCode, object request, CancellationToken cancellationToken) where T : BaseResponse
        {
            if (_disposed) throw new ObjectDisposedException(VhLogger.FormatTypeName(this));

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
            using var mem = new MemoryStream();
            mem.WriteByte(1);
            mem.WriteByte((byte)requestCode);
            await StreamUtil.WriteJsonAsync(mem, request, cancellationToken);

            // send the request
            await stream.WriteAsync(mem.ToArray());

            // Reading the response
            var response = await StreamUtil.ReadJsonAsync<T>(stream, cancellationToken);
            VhLogger.Instance.LogTrace(eventId, $"Received a response... ResponseCode: {response.ResponseCode}");

            // set SessionStatus
            if (response.AccessUsage != null)
                SessionStatus.AccessUsage = response.AccessUsage;

            // close for any error
            switch (response.ResponseCode)
            {
                case ResponseCode.InvalidSessionId:
                case ResponseCode.SessionClosed:
                case ResponseCode.SessionSuppressedBy:
                case ResponseCode.AccessExpired:
                case ResponseCode.AccessTrafficOverflow:
                case ResponseCode.UnsupportedClient:
                    SessionStatus.ResponseCode = response.ResponseCode;
                    SessionStatus.ErrorMessage = response.ErrorMessage;
                    SessionStatus.SuppressedBy = response.SuppressedBy;
                    Dispose();
                    throw new Exception(response.ErrorMessage);

                // Restore connected state by any ok return
                case ResponseCode.Ok:
                    if (!_disposed)
                        State = ClientState.Connected;
                    return response;

                case ResponseCode.GeneralError:
                default:
                    throw new Exception(response.ErrorMessage);
            }
        }

        private void Dispose(Exception ex)
        {
            if (SessionStatus.ResponseCode == ResponseCode.Ok)
            {
                SessionStatus.ResponseCode = ResponseCode.GeneralError;
                SessionStatus.ErrorMessage = ex.Message;
            }
            Dispose();
        }

        public void Dispose()
        {
            lock (this)
            {
                if (_disposed) return;
                _disposed = true;
            }

            if (State == ClientState.None) return;

            VhLogger.Instance.LogInformation("Disconnecting...");
            if (State == ClientState.Connecting || State == ClientState.Connected)
                State = ClientState.Disconnecting;
            _cancellationTokenSource.Cancel();

            // log suppressedBy
            if (SessionStatus.SuppressedBy == SuppressType.YourSelf) VhLogger.Instance.LogWarning($"You suppressed by a session of yourself!");
            else if (SessionStatus.SuppressedBy == SuppressType.Other) VhLogger.Instance.LogWarning($"You suppressed a session of another client!");

            // shutdown
            VhLogger.Instance.LogInformation("Shutting down...");
            _intervalCheckTimer?.Dispose();

            VhLogger.Instance.LogTrace($"Disposing {VhLogger.FormatTypeName<TcpProxyHost>()}...");
            _tcpProxyHost?.Dispose();

            VhLogger.Instance.LogTrace($"Disposing {VhLogger.FormatTypeName<Tunnel>()}...");
            if (Tunnel != null)
            {
                Tunnel.OnPacketReceived -= Tunnel_OnPacketReceived;
                Tunnel.OnChannelRemoved -= Tunnel_OnChannelRemoved;
                Tunnel.Dispose();
            }

            VhLogger.Instance.LogTrace($"Disposing {VhLogger.FormatTypeName<ProxyManager>()}...");
            _clientProxyManager.Dispose();

            // dispose NAT
            VhLogger.Instance.LogTrace($"Disposing {VhLogger.FormatTypeName(Nat)}...");
            Nat.Dispose();

            // close PacketCapture
            _packetCapture.OnStopped -= PacketCature_OnStopped;
            _packetCapture.OnPacketReceivedFromInbound -= PacketCapture_OnPacketReceivedFromInbound;
            if (!_leavePacketCaptureOpen)
            {
                VhLogger.Instance.LogTrace($"Disposing the PacketCapture...");
                _packetCapture.Dispose();
            }

            State = ClientState.Disposed;
            VhLogger.Instance.LogInformation("Bye Bye!");
        }
    }
}
