﻿using VpnHood.Messages;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VpnHood.Loggers;

namespace VpnHood.Client
{
    public class VpnHoodClient : IDisposable
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "<Pending>")]
        private ILogger _logger => Logger.Current;
        private readonly IPacketCapture _packetCapture;
        private readonly bool _leavePacketCaptureOpen;
        private TcpProxyHost _tcpProxyHost;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        internal Nat Nat { get; }
        internal Tunnel Tunnel { get; private set; }

        public long SentByteCount => Tunnel?.SentByteCount ?? 0;
        public long ReceivedByteCount => Tunnel?.ReceivedByteCount ?? 0;
        public Token Token { get; }
        public IpResolveMode IpResolveMode { get; }
        public Guid ClientId { get; }
        public ulong SessionId { get; private set; }
        public bool Connected { get; private set; }
        public IPAddress TcpProxyLoopbackAddress { get; }
        public int MinDatagramChannelCount { get; private set; } = 4;
        public IPAddress DnsAddress { get; set; }
        public event EventHandler OnStateChanged;
        public SessionStatus SessionStatus { get; } = new SessionStatus();

        public VpnHoodClient(IPacketCapture packetCapture, Guid clientId, Token token, ClientOptions options)
        {
            _packetCapture = packetCapture ?? throw new ArgumentNullException(nameof(packetCapture));
            _leavePacketCaptureOpen = options.LeavePacketCaptureOpen;
            Token = token ?? throw new ArgumentNullException(nameof(token));
            DnsAddress = options.DnsAddress ?? throw new ArgumentNullException(nameof(options.DnsAddress));
            TcpProxyLoopbackAddress = options.TcpProxyLoopbackAddress ?? throw new ArgumentNullException(nameof(options.TcpProxyLoopbackAddress));
            IpResolveMode = options.IpResolveMode;
            ClientId = clientId;
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
                OnStateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void Device_OnStopped(object sender, EventArgs e)
        {
            _logger.LogInformation("Device has been stopped!");
            Dispose();
        }

        private IPEndPoint _serverEndPoint;
        internal IPEndPoint ServerEndPoint
        {
            get
            {
                if (_serverEndPoint != null)
                    return _serverEndPoint;

                if (IpResolveMode == IpResolveMode.Dns || IpResolveMode == IpResolveMode.DnsThenToken)
                {
                    try
                    {
                        _logger.LogInformation($"Resolving IP from host name: {Logger.FormatDns(Token.DnsName)}...");
                        var hostEntry = Dns.GetHostEntry(Token.DnsName);
                        if (hostEntry.AddressList.Length > 0)
                        {
                            var ip = hostEntry.AddressList[0];
                            var serverEndPoint = Util.ParseIpEndPoint(Token.ServerEndPoint);

                            _logger.LogInformation($"{hostEntry.AddressList.Length} IP founds. {ip}:{serverEndPoint.Port} has been Selected!");
                            _serverEndPoint = new IPEndPoint(ip, serverEndPoint.Port);
                            return _serverEndPoint;
                        }
                    }
                    catch { };
                }

                if (IpResolveMode == IpResolveMode.Token || IpResolveMode == IpResolveMode.DnsThenToken)
                {
                    _logger.LogInformation($"Extracting host from the token. Host: {Logger.FormatDns(Token.ServerEndPoint)}");
                    _serverEndPoint = Util.ParseIpEndPoint(Token.ServerEndPoint);
                    return _serverEndPoint;
                }

                throw new Exception("Could not resolve Server Address!");
            }
        }

        public async Task Connect()
        {
            _ = _logger.BeginScope("Client");
            if (_disposed) throw new ObjectDisposedException(nameof(VpnHoodClient));

            if (State != ClientState.None)
                throw new Exception("Client connect has already been requested!");

            // Replace dot in version to prevent anonymous make treat it as ip.
            _logger.LogInformation($"Client is connecting. Version: {typeof(VpnHoodClient).Assembly.GetName().Version.ToString().Replace('.', ',')}");

            // Starting
            State = ClientState.Connecting;
            SessionStatus.SuppressedBy = SuppressType.None;
            SessionStatus.SuppressedTo = SuppressType.None;
            SessionStatus.ErrorMessage = null;
            SessionStatus.ResponseCode = ResponseCode.Ok;
            SessionStatus.AccessUsage = new AccessUsage();

            // Connect
            try
            {
                // Preparing tunnel
                Tunnel = new Tunnel();
                Tunnel.OnPacketArrival += Tunnel_OnPacketArrival;

                // Establish first connection and create a session
                await Task.Run(() => ConnectInternal());

                // create Tcp Proxy Host
                _logger.LogTrace($"Creating {Logger.FormatTypeName<TcpProxyHost>()}...");
                _tcpProxyHost = new TcpProxyHost(this, _packetCapture, TcpProxyLoopbackAddress);
                var _ = _tcpProxyHost.StartListening();

                // Preparing device
                _packetCapture.ProtectedIpAddress = ServerEndPoint.Address;
                _packetCapture.OnPacketArrivalFromInbound += Device_OnPacketArrivalFromInbound;
                if (!_packetCapture.Started)
                    _packetCapture.StartCapture();

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
        private void Device_OnPacketArrivalFromInbound(object sender, PacketCaptureArrivalEventArgs e)
        {
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
                    _logger.Log(LogLevel.Information, ClientEventId.DnsRequest, $"DNS request from {Logger.Format(ipPacket.SourceAddress)}:{udpPacket.SourcePort} to {Logger.Format(ipPacket.DestinationAddress)}, Map to: {Logger.Format(DnsAddress)}");

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
                    _logger.Log(LogLevel.Information, ClientEventId.DnsReply, $"DNS reply to {Logger.Format(natItem.DestinationAddress)}:{natItem.DestinationPort}");
                    ipPacket.SourceAddress = natItem.DestinationAddress;
                    udpPacket.DestinationPort = natItem.SourcePort;
                    udpPacket.UpdateCalculatedValues();
                    udpPacket.UpdateUdpChecksum();
                    ipPacket.UpdateCalculatedValues();
                    ((IPv4Packet)ipPacket).UpdateIPChecksum();
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
            if (curDatagramChannelCount >= MinDatagramChannelCount)
                return;

            _isManagaingDatagramChannels = true;

            // creating DatagramChannel
            Task.Run(() =>
            {
                for (var i = curDatagramChannelCount; i < MinDatagramChannelCount && !_cts.Token.IsCancellationRequested; i++)
                {
                    try
                    {
                        AddTcpDatagramChannel(GetSslConnectionToServer());
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Couldn't add a {Logger.FormatTypeName<TcpDatagramChannel>()}!", ex.Message);
                    }
                }
                _isManagaingDatagramChannels = false;
            }, cancellationToken: _cts.Token);
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
            try
            {
                // create tcpConnection
                var tcpClient = new TcpClient() { NoDelay = true };
                _packetCapture.ProtectSocket(tcpClient.Client);

                _logger.LogTrace($"Connecting to Server: {Logger.Format(ServerEndPoint)}...");
                tcpClient.Connect(ServerEndPoint.Address.ToString(), ServerEndPoint.Port);
                var stream = new SslStream(tcpClient.GetStream(), true, UserCertificateValidationCallback);

                // Establish a SSL connection
                _logger.LogTrace($"TLS Authenticating. HostName: {Logger.FormatDns(Token.DnsName)}...");
                stream.AuthenticateAsClient(Token.DnsName);

                return new TcpClientStream(tcpClient, stream);

            }
            catch
            {
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

            _logger.LogTrace($"Sending hello request. ClientId: {Logger.FormatId(ClientId)}...");
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
                ClientId = ClientId,
                TokenId = Token.TokenId,
                EncryptedClientId = encryptedClientId,
            };

            // write hello to stream
            Util.Stream_WriteJson(requestStream, request);
            requestStream.Position = 0;
            requestStream.CopyTo(tcpClientStream.Stream);

            // read response json
            _logger.LogTrace($"Waiting for hello response...");
            var helloResponse = Util.Stream_ReadJson<HelloResponse>(tcpClientStream.Stream);

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
            if (SessionId == 0)
                throw new Exception($"Could not extract SessionId!");

            _logger.LogInformation($"Hurray! Client has connected! SessionId: {Logger.FormatId(SessionId)}");

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
            using var _ = _logger.BeginScope($"{Logger.FormatTypeName<TcpDatagramChannel>()}, LocalPort: {((IPEndPoint)tcpClientStream.TcpClient.Client.LocalEndPoint).Port}");
            _logger.LogTrace($"Sending request...");

            // sending SessionId
            using var mem = new MemoryStream();
            mem.WriteByte(1);
            mem.WriteByte((byte)RequestCode.TcpDatagramChannel);
            mem.Write(BitConverter.GetBytes(SessionId));
            tcpClientStream.Stream.Write(mem.ToArray());

            // Read the response
            var response = Util.Stream_ReadJson<ChannelResponse>(tcpClientStream.Stream);

            // set SessionStatus
            SessionStatus.AccessUsage = response.AccessUsage;
            SessionStatus.ResponseCode = response.ResponseCode;
            SessionStatus.ErrorMessage = response.ErrorMessage;
            SessionStatus.SuppressedBy = response.SuppressedBy;

            // close for any error
            if (response.ResponseCode != ResponseCode.Ok)
            {
                Dispose(); // close the connection
                throw new Exception(response.ErrorMessage);
            }

            // add the channel
            _logger.LogTrace($"Creating a channel...");
            var channel = new TcpDatagramChannel(tcpClientStream);
            Tunnel.AddChannel(channel);
        }

        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _cts.Cancel();


            if (State == ClientState.Connecting || State == ClientState.Connected)
                State = ClientState.Disconnecting;

            // log suppressedBy
            if (SessionStatus.SuppressedBy == SuppressType.YourSelf) _logger.LogWarning($"You suppressed by a session of yourself!");
            else if (SessionStatus.SuppressedBy == SuppressType.Other) _logger.LogWarning($"You suppressed a session of another client!");

            // shutdown
            _logger.LogInformation("Shutting down...");
            _packetCapture.OnPacketArrivalFromInbound -= Device_OnPacketArrivalFromInbound;

            _logger.LogTrace($"Disposing {Logger.FormatTypeName<TcpProxyHost>()}...");
            _tcpProxyHost?.Dispose();

            _logger.LogTrace($"Disposing {Logger.FormatTypeName<Tunnel>()}...");
            Tunnel?.Dispose();

            _logger.LogTrace($"Disposing {Logger.FormatTypeName(Nat)}...");
            Nat.Dispose();

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
