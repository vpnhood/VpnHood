using VpnHood.Logging;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using VpnHood.Tunneling;
using VpnHood.Tunneling.Messages;
using VpnHood.Client.Device;
using System.Collections.Generic;

namespace VpnHood.Client
{
    class TcpProxyHost : IDisposable
    {
        private readonly IPAddress _loopbackAddress;
        private readonly TcpListener _tcpListener;
        private readonly IPacketCapture _packetCapture;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly List<IPPacket> _ipArivalPackets = new();
        private IPEndPoint _localEndpoint;
        private bool _disposed;

        private VpnHoodClient Client { get; }
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "<Pending>")]
        private ILogger _logger => VhLogger.Instance;

        public TcpProxyHost(VpnHoodClient client, IPacketCapture packetCapture, IPAddress loopbackAddress)
        {
            if (!client.Connected)
                throw new Exception($"{typeof(TcpProxyHost).Name}: is not connected!");

            Client = client ?? throw new ArgumentNullException(nameof(client));
            _packetCapture = packetCapture ?? throw new ArgumentNullException(nameof(packetCapture));
            _loopbackAddress = loopbackAddress ?? throw new ArgumentNullException(nameof(loopbackAddress));
            _tcpListener = new TcpListener(IPAddress.Any, 0);
        }

        public async Task StartListening()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TcpProxyHost));

            using var _ = _logger.BeginScope($"{VhLogger.FormatTypeName<TcpProxyHost>()}");
            var cancellationToken = _cancellationTokenSource.Token;

            try
            {
                _logger.LogInformation($"Start listening on {VhLogger.Format(_tcpListener.LocalEndpoint)}...");
                _tcpListener.Start();
                _localEndpoint = (IPEndPoint)_tcpListener.LocalEndpoint; //it is slow; make sure to cache it
                _packetCapture.OnPacketArrivalFromInbound += PacketCapture_OnPacketArrivalFromInbound;

                using (cancellationToken.Register(() => _tcpListener.Stop()))
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var tcpClient = await _tcpListener.AcceptTcpClientAsync();
                        tcpClient.NoDelay = true;
                        var task = ProcessClient(tcpClient, cancellationToken);
                    }
                }
            }
            catch (Exception ex)
            {
                if (!(ex is ObjectDisposedException))
                    _logger.LogError($"{ex.Message}");
            }
            finally
            {
                _logger.LogInformation($"Listener has been closed.");
            }
        }

        private void PacketCapture_OnPacketArrivalFromInbound(object sender, PacketCaptureArrivalEventArgs e)
        {
            try
            {
                _ipArivalPackets.Clear(); // prevent reallocation in this intensive event
                var ipPackets = _ipArivalPackets;

                foreach (var arivalPacket in e.ArivalPackets)
                {
                    var ipPacket = arivalPacket.IpPacket;
                    if (arivalPacket.IsHandled || ipPacket.Version != IPVersion.IPv4 || ipPacket.Protocol != PacketDotNet.ProtocolType.Tcp)
                        continue;

                    // extract tcpPacket
                    var tcpPacket = ipPacket.Extract<TcpPacket>();

                    if (Equals(ipPacket.DestinationAddress, _loopbackAddress))
                    {
                        // redirect to inbound
                        var natItem = (NatItemEx)Client.Nat.Resolve(ipPacket.Protocol, tcpPacket.DestinationPort);
                        if (natItem == null)
                        {
                            _logger.LogWarning($"Could not find item in NAT! Packet has been dropped. DesPort: {ipPacket.Protocol}:{tcpPacket.DestinationPort}");
                            //var resetPacket = Nat.BuildTcpResetPacket(ipPacket.DestinationAddress, tcpPacket.DestinationPort, ipPacket.SourceAddress, tcpPacket.SourcePort);
                            //_packetCapture.SendPacketToInbound(new[] { resetPacket }); //todo
                            arivalPacket.IsHandled = true;
                            continue;
                        }

                        ipPacket.SourceAddress = natItem.DestinationAddress;
                        ipPacket.DestinationAddress = natItem.SourceAddress;
                        tcpPacket.SourcePort = natItem.DestinationPort;
                        tcpPacket.DestinationPort = natItem.SourcePort;
                    }
                    // Redirect outbound to the local address
                    else
                    {
                        var natItem = Client.Nat.GetOrAdd(ipPacket);
                        tcpPacket.SourcePort = natItem.NatId; // 1
                        ipPacket.DestinationAddress = ipPacket.SourceAddress; // 2
                        ipPacket.SourceAddress = _loopbackAddress; //3
                        tcpPacket.DestinationPort = (ushort)_localEndpoint.Port; //4
                    }

                    ipPacket.UpdateCalculatedValues(); 
                    //todo may not needed
                    tcpPacket.UpdateTcpChecksum();
                    ((IPv4Packet)ipPacket).UpdateIPChecksum();

                    arivalPacket.IsHandled = true;
                    ipPackets.Add(ipPacket);
                }

                // send packets
                if (ipPackets.Count > 0)
                    _packetCapture.SendPacketToInbound(ipPackets.ToArray());
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogError($"{VhLogger.FormatTypeName(this)}: Error in processing packet! Error: {ex}");
            }
        }

        private async Task ProcessClient(TcpClient tcpOrgClient, CancellationToken cancellationToken)
        {
            try
            {
                // get original remote from NAT
                var orgRemoteEndPoint = (IPEndPoint)tcpOrgClient.Client.RemoteEndPoint;
                var natItem = (NatItemEx)Client.Nat.Resolve(PacketDotNet.ProtocolType.Tcp, (ushort)orgRemoteEndPoint.Port);
                if (natItem == null)
                    throw new Exception($"Could not resolve original remote from NAT! RemoteEndPoint: {VhLogger.Format(tcpOrgClient.Client.RemoteEndPoint)}");

                // create a scope for the logger
                using var _ = _logger.BeginScope($"LocalPort: {natItem.SourcePort}, RemoteEp: {natItem.DestinationAddress}:{natItem.DestinationPort}");
                _logger.LogTrace(GeneralEventId.StreamChannel, $"New TcpProxy Request.");

                // check invalid income (only voidClient accepted)
                if (!Equals(orgRemoteEndPoint.Address, _loopbackAddress))
                    throw new Exception($"TcpProxy rejected the outband connection!");

                // Create the Request
                var request = new TcpProxyChannelRequest()
                {
                    SessionId = Client.SessionId,
                    SessionKey = Client.SessionKey,
                    DestinationAddress = natItem.DestinationAddress.ToString(),
                    DestinationPort = natItem.DestinationPort,
                    CipherLength = natItem.DestinationPort == 443 ? TunnelUtil.TlsHandshakeLength : -1,
                    CipherKey = Guid.NewGuid().ToByteArray()
                };

                var tcpProxyClientStream = await Client.GetSslConnectionToServer(GeneralEventId.StreamChannel, cancellationToken);
                tcpProxyClientStream.TcpClient.ReceiveTimeout = tcpOrgClient.ReceiveTimeout;
                tcpProxyClientStream.TcpClient.ReceiveBufferSize = tcpOrgClient.ReceiveBufferSize;
                tcpProxyClientStream.TcpClient.SendBufferSize = tcpOrgClient.SendBufferSize;
                tcpProxyClientStream.TcpClient.SendTimeout = tcpOrgClient.SendTimeout;
                tcpProxyClientStream.TcpClient.NoDelay = tcpOrgClient.NoDelay;

                // read the response
                var response = await Client.SendRequest<BaseResponse>(tcpProxyClientStream.Stream, RequestCode.TcpProxyChannel, request, cancellationToken);

                // create a TcpProxyChannel
                _logger.LogTrace(GeneralEventId.StreamChannel, $"Adding a channel to session {VhLogger.FormatSessionId(request.SessionId)}...");
                var orgTcpClientStream = new TcpClientStream(tcpOrgClient, tcpOrgClient.GetStream());

                // Dispose ssl strean and repalce it with a HeadCryptor
                tcpProxyClientStream.Stream.Dispose();
                tcpProxyClientStream.Stream = StreamHeadCryptor.Create(tcpProxyClientStream.TcpClient.GetStream(),
                    request.CipherKey, null, request.CipherLength);

                var channel = new TcpProxyChannel(orgTcpClientStream, tcpProxyClientStream);
                Client.Tunnel.AddChannel(channel);
            }
            catch (Exception ex)
            {
                tcpOrgClient.Dispose();

                // logging
                _logger.LogError(GeneralEventId.StreamChannel, $"{ex.Message}");

                // Close session
                if (Client.SessionStatus.ResponseCode != ResponseCode.Ok)
                    Client.Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            _cancellationTokenSource.Cancel();
            _tcpListener.Stop();
            _packetCapture.OnPacketArrivalFromInbound -= PacketCapture_OnPacketArrivalFromInbound;
        }
    }
}
