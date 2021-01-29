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
using System.Reflection;

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
        private DateTime _reconnectTime = DateTime.MinValue;
        private readonly int _minDatagramChannelCount;
        private readonly int _reconnectDelay;
        private bool _isDisposed;

        internal Nat Nat { get; }
        internal Tunnel Tunnel { get; private set; }
        public int Timeout { get; set; }
        public int ReconnectCount { get; private set; }
        public long SentByteCount => Tunnel?.SentByteCount ?? 0;
        public long ReceivedByteCount => Tunnel?.ReceivedByteCount ?? 0;
        public Token Token { get; }
        public Guid ClientId { get; }
        public ulong SessionId { get; private set; }
        public string ServerId { get; private set; }
        public bool Connected { get; private set; }
        public IPAddress TcpProxyLoopbackAddress { get; }
        public IPAddress DnsAddress { get; set; }
        public event EventHandler StateChanged;
        public SessionStatus SessionStatus { get; private set; }
        public int MaxReconnectCount { get; }
        public string Version { get; }

        public VpnHoodClient(IPacketCapture packetCapture, Guid clientId, Token token, ClientOptions options)
        {
            _packetCapture = packetCapture ?? throw new ArgumentNullException(nameof(packetCapture));
            _reconnectDelay = options.ReconnectDelay;
            _leavePacketCaptureOpen = options.LeavePacketCaptureOpen;
            _minDatagramChannelCount = options.MinDatagramChannelCount;
            Token = token ?? throw new ArgumentNullException(nameof(token));
            DnsAddress = options.DnsAddress ?? throw new ArgumentNullException(nameof(options.DnsAddress));
            TcpProxyLoopbackAddress = options.TcpProxyLoopbackAddress ?? throw new ArgumentNullException(nameof(options.TcpProxyLoopbackAddress));
            ClientId = clientId;
            Timeout = options.Timeout;
            Version = options.Version;
            MaxReconnectCount = options.MaxReconnectCount;
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
        public IPEndPoint ServerEndPoint
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
                if (_packetCapture.IsExcludeNetworksSupported)
                    _packetCapture.ExcludeNetworks = new IPNetwork[] { new IPNetwork(ServerEndPoint.Address) };

                _packetCapture.OnPacketArrivalFromInbound += Device_OnPacketArrivalFromInbound;
                if (!_packetCapture.Started)
                    _packetCapture.StartCapture();

                State = ClientState.Connected;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error! {ex}");
                Disconnect();
                throw;
            }
        }

        // WARNING: Performance Critical!
        private void Device_OnPacketArrivalFromInbound(object sender, PacketCaptureArrivalEventArgs e)
        {
            if (_cancellationTokenSource.IsCancellationRequested) return;
            if (e.IsHandled || e.IpPacket.Version != IPVersion.IPv4) return;
            if (e.IpPacket.Protocol == PacketDotNet.ProtocolType.Udp || e.IpPacket.Protocol == PacketDotNet.ProtocolType.Icmp)
            {
                ManageDatagramChannels();
                UpdateDnsRequest(e.IpPacket, true);
                Tunnel.SendPacket(e.IpPacket);
                e.IsHandled = true;
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
                    _logger.Log(LogLevel.Information, ClientEventId.DnsRequest, $"DNS request from {VhLogger.Format(ipPacket.SourceAddress)}:{udpPacket.SourcePort} to {VhLogger.Format(ipPacket.DestinationAddress)}, Map to: {VhLogger.Format(DnsAddress)}");

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
                    _logger.Log(LogLevel.Information, ClientEventId.DnsReply, $"DNS reply to {VhLogger.Format(natItem.DestinationAddress)}:{natItem.DestinationPort}");
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
            if (_isManagaingDatagramChannels)
                return;

            // make sure there is enough DatagramChannel
            var curDatagramChannelCount = Tunnel.DatagramChannels.Length;
            if (curDatagramChannelCount >= _minDatagramChannelCount)
                return;

            _isManagaingDatagramChannels = true;

            // creating DatagramChannel
            Task.Run(() =>
            {
                for (var i = curDatagramChannelCount; i < _minDatagramChannelCount && !_cancellationTokenSource.Token.IsCancellationRequested; i++)
                {
                    try
                    {
                        AddTcpDatagramChannel(GetSslConnectionToServer());
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Couldn't add a {VhLogger.FormatTypeName<TcpDatagramChannel>()}!", ex.Message);
                    }
                }
                _isManagaingDatagramChannels = false;
            }, cancellationToken: _cancellationTokenSource.Token);
        }

        // WARNING: Performance Critical!
        private void Tunnel_OnPacketArrival(object sender, ChannelPacketArrivalEventArgs e)
        {
            // manage DNS reply
            UpdateDnsRequest(e.IpPacket, false);

            // forward packet to device
            _packetCapture.SendPacketToInbound(e.IpPacket);
        }

        internal TcpClientStream GetSslConnectionToServer()
        {
            var tcpClient = new TcpClient() { NoDelay = true };
            try
            {
                // create tcpConnection
                _packetCapture.ProtectSocket(tcpClient.Client);

                _logger.LogTrace($"Connecting to Server: {VhLogger.Format(ServerEndPoint)}...");
                var task = tcpClient.ConnectAsync(ServerEndPoint.Address, ServerEndPoint.Port);
                Task.WaitAny(new[] { task }, Timeout);
                if (!tcpClient.Connected) 
                    throw new TimeoutException();

                // start TLS
                var stream = new SslStream(tcpClient.GetStream(), true, UserCertificateValidationCallback);

                // Establish a TLS connection
                _logger.LogTrace($"TLS Authenticating. HostName: {VhLogger.FormatDns(Token.DnsName)}...");
                stream.AuthenticateAsClient(Token.DnsName);

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
            return
                sslPolicyErrors == SslPolicyErrors.None ||
                Token.PublicKeyHash == null ||
                Token.ComputePublicKeyHash(certificate.GetPublicKey()).SequenceEqual(Token.PublicKeyHash);
        }

        private void ConnectInternal()
        {
            var tcpClientStream = GetSslConnectionToServer();

            _logger.LogTrace($"Sending hello request. ClientId: {VhLogger.FormatId(ClientId)}...");
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
            };

            // write hello to stream
            TunnelUtil.Stream_WriteJson(requestStream, request);
            requestStream.Position = 0;
            requestStream.CopyTo(tcpClientStream.Stream);

            // read response json
            _logger.LogTrace($"Waiting for hello response...");
            var helloResponse = TunnelUtil.Stream_ReadJson<HelloResponse>(tcpClientStream.Stream);

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
            ServerId = helloResponse.ServerId;
            if (SessionId == 0)
                throw new Exception($"Could not extract SessionId!");

            _logger.LogInformation($"Hurray! Client has connected! SessionId: {VhLogger.FormatId(SessionId)}");

            // report Suppressed
            if (helloResponse.SuppressedTo == SuppressType.YourSelf) _logger.LogWarning($"You suppressed a session of yourself!");
            else if (helloResponse.SuppressedTo == SuppressType.Other) _logger.LogWarning($"You suppressed a session of another client!");

            // add current stream as a channel
            _logger.LogTrace($"Adding Hello stream as a TcpDatagram Channel...");
            AddTcpDatagramChannel(tcpClientStream);
            Connected = true;
        }

        private void AddTcpDatagramChannel(TcpClientStream tcpClientStream)
        {
            using var _ = _logger.BeginScope($"{VhLogger.FormatTypeName<TcpDatagramChannel>()}, LocalPort: {((IPEndPoint)tcpClientStream.TcpClient.Client.LocalEndPoint).Port}");
            _logger.LogTrace($"Sending request...");

            // sending SessionId
            using var mem = new MemoryStream();
            mem.WriteByte(2); //Version 2!
            mem.WriteByte((byte)RequestCode.TcpDatagramChannel);

            // Create the Request Message
            var request = new TcpDatagramChannelRequest()
            {
                SessionId = SessionId,
                ServerId = ServerId,
            };
            TunnelUtil.Stream_WriteJson(mem, request);
            tcpClientStream.Stream.Write(mem.ToArray());

            // Read the response
            var response = TunnelUtil.Stream_ReadJson<ChannelResponse>(tcpClientStream.Stream);

            // set SessionStatus
            SessionStatus.ResponseCode = response.ResponseCode;
            SessionStatus.ErrorMessage = response.ErrorMessage;
            SessionStatus.SuppressedBy = response.SuppressedBy;
            if (response.AccessUsage!=null) SessionStatus.AccessUsage = response.AccessUsage;

            // close for any error
            if (response.ResponseCode != ResponseCode.Ok)
            {
                Disconnect();
                throw new Exception(response.ErrorMessage);
            }

            // add the channel
            _logger.LogTrace($"Creating a channel...");
            var channel = new TcpDatagramChannel(tcpClientStream);
            Tunnel.AddChannel(channel);
        }

        internal void Disconnect()
        {
            // reset _reconnectCount if last reconnect was more than 5 minutes ago
            if ((DateTime.Now - _reconnectTime).TotalMinutes > 5)
                ReconnectCount = 0;

            // check reconnecting
            if (State == ClientState.Connected && // client must already connected
                ReconnectCount < MaxReconnectCount && // check MaxReconnectCount
                (SessionStatus.ResponseCode == ResponseCode.GeneralError || SessionStatus.ResponseCode == ResponseCode.SessionClosed || SessionStatus.ResponseCode == ResponseCode.InvalidSessionId))
            {
                _reconnectTime = DateTime.Now;
                ReconnectCount++;
                DisconnectInternal();
                Task.Delay(_reconnectDelay).ContinueWith((task) =>
                {
                    _state = ClientState.None;
                    var _ = Connect();
                });
                return;
            }
            Dispose();
        }

        private void DisconnectInternal()
        {
            if (_isDisposed) return;
            if (State == ClientState.None) return;

            _logger.LogInformation("Disconnecting...");
            if (State == ClientState.Connecting || State == ClientState.Connected)
                State = ClientState.Disconnecting;

            _cancellationTokenSource.Cancel();

            // log suppressedBy
            if (SessionStatus.SuppressedBy == SuppressType.YourSelf) _logger.LogWarning($"You suppressed by a session of yourself!");
            else if (SessionStatus.SuppressedBy == SuppressType.Other) _logger.LogWarning($"You suppressed a session of another client!");

            _packetCapture.OnPacketArrivalFromInbound -= Device_OnPacketArrivalFromInbound;

            _logger.LogTrace($"Disposing {VhLogger.FormatTypeName<TcpProxyHost>()}...");
            _tcpProxyHost?.Dispose();

            _logger.LogTrace($"Disposing {VhLogger.FormatTypeName<Tunnel>()}...");
            Tunnel?.Dispose();
        }


        public void Dispose()
        {
            if (_isDisposed) return;

            // shutdown
            _logger.LogInformation("Shutting down...");

            // disconnect
            DisconnectInternal();
            _isDisposed = true; // must after DisconnectInternal

            // dispose NAT
            _logger.LogTrace($"Disposing {VhLogger.FormatTypeName(Nat)}...");
            Nat.Dispose();

            // close PacketCapture
            if (!_leavePacketCaptureOpen)
            {
                _logger.LogTrace($"Disposing Capturing Device...");
                _packetCapture.Dispose();
            }

            _logger.LogInformation("Bye Bye!");
            State = ClientState.Disposed;
        }
    }
}
