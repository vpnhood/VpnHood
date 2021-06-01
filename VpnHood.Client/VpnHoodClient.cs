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

namespace VpnHood.Client
{
    public class VpnHoodClient : IDisposable
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "<Pending>")]
        private ILogger _logger => VhLogger.Current;
        private readonly IPacketCapture _packetCapture;
        private readonly bool _leavePacketCaptureOpen;
        private TcpProxyHost _tcpProxyHost;
        private CancellationTokenSource _cancellationTokenSource;
        private readonly int _minTcpDatagramChannelCount;
        private bool _isDisposed;
        private UdpChannel _udpChannel;
        internal Nat Nat { get; }
        internal Tunnel Tunnel { get; private set; }
        public int Timeout { get; set; }
        public Token Token { get; }
        public Guid ClientId { get; }
        public int SessionId { get; private set; }
        public string ServerId { get; private set; }
        public byte[] SessionKey { get; private set; }
        public IPEndPoint ServerUdpEndPoint { get; private set; }
        public bool Connected { get; private set; }
        public IPAddress TcpProxyLoopbackAddress { get; }
        public IPAddress DnsAddress { get; set; }
        public event EventHandler StateChanged;
        public SessionStatus SessionStatus { get; private set; }
        public string Version { get; }
        public long ReceiveSpeed => Tunnel?.ReceiveSpeed ?? 0;
        public long ReceivedByteCount => Tunnel?.ReceivedByteCount ?? 0;
        public long SendSpeed => Tunnel?.SendSpeed ?? 0;
        public long SentByteCount => Tunnel?.SentByteCount ?? 0;
        public bool UseUdpChannel { get; set; }

        public VpnHoodClient(IPacketCapture packetCapture, Guid clientId, Token token, ClientOptions options)
        {
            _packetCapture = packetCapture ?? throw new ArgumentNullException(nameof(packetCapture));
            _leavePacketCaptureOpen = options.LeavePacketCaptureOpen;
            _minTcpDatagramChannelCount = options.MinTcpDatagramChannelCount;
            Token = token ?? throw new ArgumentNullException(nameof(token));
            DnsAddress = options.DnsAddress ?? throw new ArgumentNullException(nameof(options.DnsAddress));
            TcpProxyLoopbackAddress = options.TcpProxyLoopbackAddress ?? throw new ArgumentNullException(nameof(options.TcpProxyLoopbackAddress));
            ClientId = clientId;
            Timeout = options.Timeout;
            Version = options.Version;
            UseUdpChannel = options.UseUdpChannel;
            Nat = new Nat(true);

            packetCapture.OnStopped += Device_OnStopped;
        }

        private ClientState _state = ClientState.None;
        public ClientState State
        {
            get => _state;
            private set
            {
                if (_state == value) return;
                _state = value;
                _logger.LogInformation($"Client is {State}");
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void Device_OnStopped(object sender, EventArgs e)
        {
            _logger.LogInformation("Device has been stopped!");
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
                        _logger.LogInformation($"Resolving IP from host name: {VhLogger.FormatDns(Token.DnsName)}...");
                        var hostEntry = Dns.GetHostEntry(Token.DnsName);
                        if (hostEntry.AddressList.Length > 0)
                        {
                            var index = random.Next(0, hostEntry.AddressList.Length);
                            var ip = hostEntry.AddressList[index];
                            var serverEndPoint = Util.ParseIpEndPoint(Token.ServerEndPoint);

                            _logger.LogInformation($"{hostEntry.AddressList.Length} IP founds. {ip}:{serverEndPoint.Port} has been Selected!");
                            _serverEndPoint = new IPEndPoint(ip, serverEndPoint.Port);
                            return _serverEndPoint;
                        }
                    }
                    catch { };
                }
                else
                {
                    _logger.LogInformation($"Extracting host from the token. Host: {VhLogger.FormatDns(Token.ServerEndPoint)}");
                    var index = random.Next(0, Token.ServerEndPoints.Length);
                    _serverEndPoint = Util.ParseIpEndPoint(Token.ServerEndPoints[index]);
                    return _serverEndPoint;
                }

                throw new Exception("Could not resolve Server Address!");
            }
        }

        public async Task Connect()
        {
            _ = _logger.BeginScope("Client");
            if (_isDisposed) throw new ObjectDisposedException(nameof(VpnHoodClient));

            if (State != ClientState.None)
                throw new Exception("Connection is already in progress!");

            // Replace dot in version to prevent anonymous make treat it as ip.
            _logger.LogInformation($"Client is connecting. Version: {Version}");

            // Starting
            State = ClientState.Connecting;
            SessionStatus = new SessionStatus();
            _cancellationTokenSource = new CancellationTokenSource();

            // Connect
            try
            {
                // Preparing tunnel
                Tunnel = new Tunnel();
                Tunnel.OnPacketArrival += Tunnel_OnPacketArrival;

                // Establish first connection and create a session
                await Task.Run(() => ConnectInternal());

                // create Tcp Proxy Host
                _logger.LogTrace($"Creating {VhLogger.FormatTypeName<TcpProxyHost>()}...");
                _tcpProxyHost = new TcpProxyHost(this, _packetCapture, TcpProxyLoopbackAddress);
                var _ = _tcpProxyHost.StartListening();

                // Preparing device
                if (!_packetCapture.Started)
                {
                    // Exclude serverEp
                    if (_packetCapture.IsExcludeNetworksSupported)
                        _packetCapture.ExcludeNetworks = _packetCapture.ExcludeNetworks != null
                            ? _packetCapture.ExcludeNetworks.Concat(new IPNetwork[] { new IPNetwork(ServerTcpEndPoint.Address) }).ToArray()
                            : new IPNetwork[] { new IPNetwork(ServerTcpEndPoint.Address) }.ToArray();

                    _packetCapture.OnPacketArrivalFromInbound += PacketCapture_OnPacketArrivalFromInbound;
                    _packetCapture.StartCapture();
                }

                State = ClientState.Connected;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error! {ex}");
                Dispose();
                throw;
            }
        }

        // WARNING: Performance Critical!
        private void PacketCapture_OnPacketArrivalFromInbound(object sender, PacketCaptureArrivalEventArgs e)
        {
            try
            {
                ManageDatagramChannels();

                var ipPackets = new List<IPPacket>();
                foreach (var arivalPacket in e.ArivalPackets)
                {
                    var ipPacket = arivalPacket.IpPacket;
                    if (_cancellationTokenSource.IsCancellationRequested) return;
                    if (arivalPacket.IsHandled || ipPacket.Version != IPVersion.IPv4)
                        continue;


                    // tunnel only Udp and Icmp packets
                    if (ipPacket.Protocol == PacketDotNet.ProtocolType.Udp || ipPacket.Protocol == PacketDotNet.ProtocolType.Icmp)
                    {
                        UpdateDnsRequest(ipPacket, true);
                        arivalPacket.IsHandled = true;
                        ipPackets.Add(ipPacket);
                    }
                }

                if (ipPackets.Count > 0)
                    Tunnel.SendPacket(ipPackets.ToArray());

            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void UpdateDnsRequest(IPPacket ipPacket, bool outgoing)
        {
            if (ipPacket.Protocol != PacketDotNet.ProtocolType.Udp) return;

            // manage DNS outgoing packet if requested DNS is not VPN DNS
            if (outgoing && !ipPacket.DestinationAddress.Equals(DnsAddress))
            {
                var udpPacket = ipPacket.Extract<UdpPacket>();
                if (udpPacket.DestinationPort == 53) //53 is DNS port
                {
                    _logger.Log(LogLevel.Information, GeneralEventId.Dns, $"DNS request from {VhLogger.Format(ipPacket.SourceAddress)}:{udpPacket.SourcePort} to {VhLogger.Format(ipPacket.DestinationAddress)}, Map to: {VhLogger.Format(DnsAddress)}");

                    udpPacket.SourcePort = Nat.GetOrAdd(ipPacket).NatId;
                    udpPacket.UpdateCalculatedValues();
                    udpPacket.UpdateUdpChecksum();
                    ipPacket.DestinationAddress = DnsAddress;
                    ipPacket.UpdateCalculatedValues();
                    ((IPv4Packet)ipPacket).UpdateIPChecksum();
                }
            }

            // manage DNS incomming packet from VPN DNS
            else if (!outgoing && ipPacket.SourceAddress.Equals(DnsAddress))
            {
                var udpPacket = ipPacket.Extract<UdpPacket>();
                var natItem = (NatItemEx)Nat.Resolve(PacketDotNet.ProtocolType.Udp, udpPacket.DestinationPort);
                if (natItem != null)
                {
                    _logger.Log(LogLevel.Information, GeneralEventId.Dns, $"DNS reply to {VhLogger.Format(natItem.DestinationAddress)}:{natItem.DestinationPort}");
                    ipPacket.SourceAddress = natItem.DestinationAddress;
                    udpPacket.DestinationPort = natItem.SourcePort;
                    udpPacket.UpdateCalculatedValues();
                    udpPacket.UpdateUdpChecksum();
                    ((IPv4Packet)ipPacket).UpdateIPChecksum();
                    ipPacket.UpdateCalculatedValues();
                }
            }
        }

        private bool _isManagaingDatagramChannels;
        private void ManageDatagramChannels()
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
                if (!ManageDatagramChannelsInternal())
                    _isManagaingDatagramChannels = false;
            }
            catch
            {
                _isManagaingDatagramChannels = false;
                throw;
            }
        }

        /// <returns>true if managing is in progress</returns>
        private bool ManageDatagramChannelsInternal()
        {
            // make sure only one UdpChannel exists for DatagramChannels if  UseUdpChannel is on
            if (UseUdpChannel && ServerUdpEndPoint != null)
            {
                // check current channels
                if (Tunnel.DatagramChannels.Length == 1 && Tunnel.DatagramChannels[0] is UdpChannel)
                    return false;

                // remove all other datagram channel
                foreach (var channel in Tunnel.DatagramChannels.ToArray())
                    Tunnel.RemoveChannel(channel, true);

                // add the only one udp channel
                Tunnel.AddChannel(_udpChannel);
                return false;
            }

            // remove UDP datagram channels
            foreach (var channel in Tunnel.DatagramChannels.ToArray())
                if (channel is UdpChannel)
                    Tunnel.RemoveChannel(channel, true);

            // make sure there is enough DatagramChannel
            var curDatagramChannelCount = Tunnel.DatagramChannels.Length;
            if (curDatagramChannelCount < _minTcpDatagramChannelCount)
                return false;

            // creating DatagramChannels
            List<Task> tasks = new();
            for (var i = curDatagramChannelCount; i < _minTcpDatagramChannelCount; i++)
                tasks.Add(AddTcpDatagramChannel());

            Task.WhenAll().ContinueWith(x =>
            {
                if (x.IsFaulted)
                    _logger.LogError($"Couldn't add a {VhLogger.FormatTypeName<TcpDatagramChannel>()}!", x.Exception);
                _isManagaingDatagramChannels = false;
            });

            return true;
        }

        // WARNING: Performance Critical!
        private void Tunnel_OnPacketArrival(object sender, ChannelPacketArrivalEventArgs e)
        {
            // manage DNS reply
            foreach (var ipPacket in e.IpPackets)
                UpdateDnsRequest(ipPacket, false);

            // forward packet to device
            _packetCapture.SendPacketToInbound(e.IpPackets);
        }

        internal async Task<TcpClientStream> GetSslConnectionToServer(EventId eventId)
        {
            var tcpClient = new TcpClient() { NoDelay = true };
            try
            {
                // create tcpConnection
                _packetCapture.ProtectSocket(tcpClient.Client);

                // Client.Timeout does not affect in ConnectAsync
                _logger.LogTrace(eventId, $"Connecting to Server: {VhLogger.Format(ServerTcpEndPoint)}...");
                var connectTask = tcpClient.ConnectAsync(ServerTcpEndPoint.Address, ServerTcpEndPoint.Port);
                var cancelTask = Task.Delay(Timeout);
                await Task.WhenAny(connectTask, cancelTask);
                if (!tcpClient.Connected)
                    throw new TimeoutException();

                // start TLS
                var stream = new SslStream(tcpClient.GetStream(), true, UserCertificateValidationCallback);

                // Establish a TLS connection
                _logger.LogTrace(eventId, $"TLS Authenticating. HostName: {VhLogger.FormatDns(Token.DnsName)}...");
                await stream.AuthenticateAsClientAsync(Token.DnsName);

                // Restore connected state if SessionId is set
                if (SessionId != 0 && SessionStatus?.ResponseCode == ResponseCode.Ok)
                    State = ClientState.Connected;

                return new TcpClientStream(tcpClient, stream);
            }
            catch
            {
                tcpClient?.Dispose();
                if (State == ClientState.Connected)
                    State = ClientState.Connecting;
                throw;
            }
        }

        private bool UserCertificateValidationCallback(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return sslPolicyErrors == SslPolicyErrors.None ||
                Token.CertificateHash == null ||
                Token.CertificateHash.SequenceEqual(certificate.GetCertHash());
        }


        private async Task ConnectInternal()
        {
            var tcpClientStream = await GetSslConnectionToServer(GeneralEventId.Hello);

            _logger.LogTrace(GeneralEventId.Hello, $"Sending hello request. ClientId: {VhLogger.FormatId(ClientId)}...");
            // generate hello message and get the session id
            using var requestStream = new MemoryStream();
            requestStream.WriteByte(1);
            requestStream.WriteByte((byte)RequestCode.Hello);

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
                ClientVersion = typeof(VpnHoodClient).Assembly.GetName().Version.ToString(3),
                ClientId = ClientId,
                TokenId = Token.TokenId,
                EncryptedClientId = encryptedClientId,
                UseUdpChannel = UseUdpChannel,
            };

            // write hello to stream
            await StreamUtil.WriteJsonAsync(requestStream, request);
            requestStream.Position = 0;
            await requestStream.CopyToAsync(tcpClientStream.Stream);

            // read response json
            _logger.LogTrace(GeneralEventId.Hello, $"Waiting for hello response...");
            var helloResponse = await StreamUtil.ReadJsonAsync<HelloResponse>(tcpClientStream.Stream);

            // set SessionStatus
            SessionStatus.AccessUsage = helloResponse.AccessUsage;
            SessionStatus.ResponseCode = helloResponse.ResponseCode;
            SessionStatus.SuppressedTo = helloResponse.SuppressedTo;
            SessionStatus.ErrorMessage = helloResponse.ErrorMessage;

            // check error
            if (helloResponse.ResponseCode != ResponseCode.Ok)
                throw new Exception(helloResponse.ErrorMessage);

            // get session id
            SessionId = helloResponse.SessionId;
            SessionKey = helloResponse.SessionKey;
            ServerUdpEndPoint = helloResponse.UdpPort != 0 ? new IPEndPoint(ServerTcpEndPoint.Address, helloResponse.UdpPort) : null;
            ServerId = helloResponse.ServerId;
            if (SessionId == 0)
                throw new Exception($"Could not extract SessionId!");

            // report Suppressed
            if (helloResponse.SuppressedTo == SuppressType.YourSelf) _logger.LogWarning($"You suppressed a session of yourself!");
            else if (helloResponse.SuppressedTo == SuppressType.Other) _logger.LogWarning($"You suppressed a session of another client!");

            // add current stream as a channel
            if (UseUdpChannel && ServerUdpEndPoint != null)
            {
                // create the only one udp channel
                _logger.LogInformation(GeneralEventId.DatagramChannel, $"Creating {VhLogger.FormatTypeName<UdpChannel>()}... ServerEp: {ServerUdpEndPoint}");
                var udpClient = new UdpClient();
                udpClient.Connect(ServerUdpEndPoint);
                _udpChannel = new UdpChannel(true, udpClient, helloResponse.SessionId, helloResponse.SessionKey);
                _logger.LogInformation(GeneralEventId.DatagramChannel, $"The only one {VhLogger.FormatTypeName<UdpChannel>()} has been created. LocalEp: {udpClient.Client.LocalEndPoint}, ServerEp: {ServerUdpEndPoint}");
            }
            else
            {
                _logger.LogTrace(GeneralEventId.DatagramChannel, $"Adding Hello stream as a TcpDatagram Channel...");
                await AddTcpDatagramChannel(tcpClientStream);
            }

            // done
            _logger.LogInformation(GeneralEventId.Hello, $"Hurray! Client has connected! SessionId: {VhLogger.FormatId(SessionId)}");
            Connected = true;
        }


        private async Task<TcpDatagramChannel> AddTcpDatagramChannel()
        {
            var tcpClientStream = await GetSslConnectionToServer(GeneralEventId.DatagramChannel);
            return await AddTcpDatagramChannel(tcpClientStream);
        }

        private async Task<TcpDatagramChannel> AddTcpDatagramChannel(TcpClientStream tcpClientStream)
        {
            using var _ = _logger.BeginScope($"{VhLogger.FormatTypeName<TcpDatagramChannel>()}, LocalPort: {((IPEndPoint)tcpClientStream.TcpClient.Client.LocalEndPoint).Port}");
            _logger.LogTrace(GeneralEventId.DatagramChannel, $"Sending request...");

            // sending SessionId
            using var mem = new MemoryStream();
            mem.WriteByte(1);
            mem.WriteByte((byte)RequestCode.TcpDatagramChannel);

            // Create the Request Message
            var request = new TcpDatagramChannelRequest()
            {
                SessionId = SessionId,
                ServerId = ServerId,
            };
            await StreamUtil.WriteJsonAsync(mem, request);
            await tcpClientStream.Stream.WriteAsync(mem.ToArray());

            // Read the response
            var response = await StreamUtil.ReadJsonAsync<SessionResponse>(tcpClientStream.Stream);

            // set SessionStatus
            SessionStatus.ResponseCode = response.ResponseCode;
            SessionStatus.ErrorMessage = response.ErrorMessage;
            SessionStatus.SuppressedBy = response.SuppressedBy;
            if (response.AccessUsage != null) SessionStatus.AccessUsage = response.AccessUsage;

            // close for any error
            if (response.ResponseCode != ResponseCode.Ok)
            {
                Dispose();
                throw new Exception(response.ErrorMessage);
            }

            // add the channel
            _logger.LogTrace(GeneralEventId.DatagramChannel, $"Creating a channel...");
            var channel = new TcpDatagramChannel(tcpClientStream);
            Tunnel.AddChannel(channel);
            return channel;
        }

        private Task<bool> GetServerSessionStatus()
        {
            using var stream = GetSslConnectionToServer(GeneralEventId.StreamChannel);

            // sending SessionId
            using var mem = new MemoryStream();
            mem.WriteByte(1);
            mem.WriteByte((byte)RequestCode.SessionStatus);

            // Create the Request Message
            var request = new SessionRequest()
            {
                SessionId = SessionId,
                ServerId = ServerId,
            };
            StreamUtil.WriteJson(mem, request);
            //stream.Stream.Write(mem.ToArray());
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            lock (this)
            {
                if (_isDisposed) return;
                _isDisposed = true;
            }

            if (State == ClientState.None) return;

            _logger.LogInformation("Disconnecting...");
            if (State == ClientState.Connecting || State == ClientState.Connected)
                State = ClientState.Disconnecting;

            _cancellationTokenSource.Cancel();

            // log suppressedBy
            if (SessionStatus.SuppressedBy == SuppressType.YourSelf) _logger.LogWarning($"You suppressed by a session of yourself!");
            else if (SessionStatus.SuppressedBy == SuppressType.Other) _logger.LogWarning($"You suppressed a session of another client!");

            _packetCapture.OnPacketArrivalFromInbound -= PacketCapture_OnPacketArrivalFromInbound;

            _logger.LogTrace($"Disposing {VhLogger.FormatTypeName<TcpProxyHost>()}...");
            _tcpProxyHost?.Dispose();

            _logger.LogTrace($"Disposing {VhLogger.FormatTypeName<Tunnel>()}...");
            Tunnel?.Dispose();

            // shutdown
            _logger.LogInformation("Shutting down...");

            // dispose NAT
            _logger.LogTrace($"Disposing {VhLogger.FormatTypeName(Nat)}...");
            Nat.Dispose();

            // close PacketCapture
            if (!_leavePacketCaptureOpen)
            {
                _logger.LogTrace($"Disposing Captured Device...");
                _packetCapture.Dispose();
            }

            State = ClientState.Disposed;
            _logger.LogInformation("Bye Bye!");
        }
    }
}
