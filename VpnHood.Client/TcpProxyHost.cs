using VpnHood.Logging;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using System;
using System.IO;
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
        private readonly IPacketCapture _device;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private IPEndPoint _localEndpoint;
        private VpnHoodClient Client { get; }
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "<Pending>")]
        private ILogger _logger => VhLogger.Current;

        public TcpProxyHost(VpnHoodClient client, IPacketCapture device, IPAddress loopbackAddress)
        {
            if (!client.Connected)
                throw new Exception($"{typeof(TcpProxyHost).Name}: is not connected!");

            Client = client ?? throw new ArgumentNullException(nameof(client));
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _loopbackAddress = loopbackAddress ?? throw new ArgumentNullException(nameof(loopbackAddress));
            _tcpListener = new TcpListener(IPAddress.Any, 0);
        }

        public async Task StartListening()
        {
            using var _ = _logger.BeginScope($"{VhLogger.FormatTypeName<TcpProxyHost>()}");

            try
            {
                _logger.LogInformation($"Start listening on {VhLogger.Format(_tcpListener.LocalEndpoint)}...");
                _tcpListener.Start();
                _localEndpoint = (IPEndPoint)_tcpListener.LocalEndpoint; //it is slow; make sure to cache it
                _device.OnPacketArrivalFromInbound += Device_OnPacketArrivalFromInbound;

                while (!_cancellationTokenSource.IsCancellationRequested)
                {
                    var tcpClient = await _tcpListener.AcceptTcpClientAsync();
                    tcpClient.NoDelay = true;
                    var task = ProcessClient(tcpClient);
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

        private void Device_OnPacketArrivalFromInbound(object sender, PacketCaptureArrivalEventArgs e)
        {
            var ipPackets = new List<IPPacket>();
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
                        arivalPacket.IsHandled = true;
                        continue;
                    }

                    ipPacket.SourceAddress = natItem.DestinationAddress;
                    ipPacket.DestinationAddress = natItem.SourceAddress;
                    tcpPacket.SourcePort = natItem.DestinationPort;
                    tcpPacket.DestinationPort = natItem.SourcePort;
                    if (tcpPacket.Finished)
                        tcpPacket.Reset = tcpPacket.Reset;

                }
                else
                {
                    // Redirect to local address
                    tcpPacket.SourcePort = Client.Nat.GetOrAdd(ipPacket).NatId; // 1
                    ipPacket.DestinationAddress = ipPacket.SourceAddress; // 2
                    ipPacket.SourceAddress = _loopbackAddress; //3
                    tcpPacket.DestinationPort = (ushort)_localEndpoint.Port; //4
                }

                tcpPacket.UpdateTcpChecksum();
                ipPacket.UpdateCalculatedValues();
                ((IPv4Packet)ipPacket).UpdateIPChecksum();

                arivalPacket.IsHandled = true;
                ipPackets.Add(ipPacket);
            }

            // send packets
            if (ipPackets.Count > 0)
                _device.SendPacketToInbound(ipPackets.ToArray());
        }

        private async Task ProcessClient(TcpClient tcpOrgClient)
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
                    ServerId = Client.ServerId,
                    DestinationAddress = natItem.DestinationAddress.ToString(),
                    DestinationPort = natItem.DestinationPort,
                    CipherLength = natItem.DestinationPort == 443 ? TunnelUtil.TlsHandshakeLength : -1,
                    CipherKey = Guid.NewGuid().ToByteArray()
                };

                var tcpProxyClientStream = await Client.GetSslConnectionToServer(GeneralEventId.StreamChannel);
                tcpProxyClientStream.TcpClient.ReceiveTimeout = tcpOrgClient.ReceiveTimeout;
                tcpProxyClientStream.TcpClient.ReceiveBufferSize = tcpOrgClient.ReceiveBufferSize;
                tcpProxyClientStream.TcpClient.SendBufferSize = tcpOrgClient.SendBufferSize;
                tcpProxyClientStream.TcpClient.SendTimeout = tcpOrgClient.SendTimeout;
                tcpProxyClientStream.TcpClient.NoDelay = tcpOrgClient.NoDelay;

                // read the response
                var response = await Client.SendRequest<BaseResponse>(tcpProxyClientStream.Stream, RequestCode.TcpProxyChannel, request);

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
            }
        }

        public void Dispose()
        {
            if (_cancellationTokenSource.IsCancellationRequested)
                return;

            _device.OnPacketArrivalFromInbound -= Device_OnPacketArrivalFromInbound;
            _cancellationTokenSource.Cancel();
            _tcpListener.Stop();
        }
    }
}
