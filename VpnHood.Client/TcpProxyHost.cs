using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Common;
using VpnHood.Common.Messaging;
using VpnHood.Common.Logging;
using VpnHood.Tunneling;
using VpnHood.Tunneling.Messaging;
using ProtocolType = PacketDotNet.ProtocolType;

namespace VpnHood.Client
{
    internal class TcpProxyHost : IDisposable
    {
        private bool _disposed;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly List<IPPacket> _ipPackets = new();
        private TcpListener? _tcpListenerIpV4;
        private TcpListener? _tcpListenerIpV6;
        private IPEndPoint? _localEndpointIpV4;
        private IPEndPoint? _localEndpointIpV6;
        private VpnHoodClient Client { get; }
        public IPAddress LoopbackAddressIpV4 { get; }
        public IPAddress LoopbackAddressIpV6 { get; }

        public TcpProxyHost(VpnHoodClient client, IPAddress loopbackAddressIpV4, IPAddress loopbackAddressIpV6)
        {
            Client = client ?? throw new ArgumentNullException(nameof(client));
            LoopbackAddressIpV4 = loopbackAddressIpV4 ?? throw new ArgumentNullException(nameof(loopbackAddressIpV4));
            LoopbackAddressIpV6 = loopbackAddressIpV6 ?? throw new ArgumentNullException(nameof(loopbackAddressIpV6));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _cancellationTokenSource.Cancel();
            _tcpListenerIpV4?.Stop();
            _tcpListenerIpV6?.Stop();
        }

        public void Start()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(TcpProxyHost));
            using var logScope = VhLogger.Instance.BeginScope($"{VhLogger.FormatTypeName<TcpProxyHost>()}");
            VhLogger.Instance.LogInformation($"Starting {VhLogger.FormatTypeName(this)}...");

            // IpV4
            _tcpListenerIpV4 = new TcpListener(IPAddress.Any, 0);
            _tcpListenerIpV4.Start();
            _localEndpointIpV4 = (IPEndPoint)_tcpListenerIpV4.LocalEndpoint; //it is slow; make sure to cache it
            VhLogger.Instance.LogInformation(
                $"{VhLogger.FormatTypeName(this)} is listening on {VhLogger.Format(_localEndpointIpV4)}");
            _ = AcceptTcpClientLoop(_tcpListenerIpV4);

            // IpV6
            try
            {
                _tcpListenerIpV6 = new TcpListener(IPAddress.IPv6Any, 0);
                _tcpListenerIpV6.Start();
                _localEndpointIpV6 = (IPEndPoint)_tcpListenerIpV6.LocalEndpoint; //it is slow; make sure to cache it
                VhLogger.Instance.LogInformation(
                    $"{VhLogger.FormatTypeName(this)} is listening on {VhLogger.Format(_localEndpointIpV6)}");
                _ = AcceptTcpClientLoop(_tcpListenerIpV6);
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogError(ex, $"Could not create listener on {VhLogger.Format(new IPEndPoint(IPAddress.IPv6Any, 0))}!");
            }
        }

        private async Task AcceptTcpClientLoop(TcpListener tcpListener)
        {
            var cancellationToken = _cancellationTokenSource.Token;

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var tcpClient = await Util.RunTask(tcpListener.AcceptTcpClientAsync(), default, cancellationToken);
                    _ = ProcessClient(tcpClient, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                if (ex is not ObjectDisposedException)
                    VhLogger.Instance.LogError($"{ex.Message}");
            }
            finally
            {
                VhLogger.Instance.LogInformation("Listener has been closed.");
            }
        }

        // this method should not be called in multi-thread, the return buffer is shared and will be modified on next call
        public IPPacket[] ProcessOutgoingPacket(IPPacket[] ipPackets)
        {
            if (_localEndpointIpV4 == null)
                throw new InvalidOperationException($"{nameof(_localEndpointIpV4)} has not been initialized! Did you call {nameof(Start)}!");

            _ipPackets.Clear(); // prevent reallocation in this intensive method
            var ret = _ipPackets;

            foreach (var item in ipPackets)
            {
                var ipPacket = item;
                var loopbackAddress = ipPacket.Version == IPVersion.IPv4 ? LoopbackAddressIpV4 : LoopbackAddressIpV6;
                var localEndPoint = ipPacket.Version == IPVersion.IPv4 ? _localEndpointIpV4 : _localEndpointIpV6;
                if (localEndPoint == null)
                    continue;

                try
                {
                    if (ipPacket.Protocol != ProtocolType.Tcp)
                        throw new InvalidOperationException($"{typeof(TcpProxyHost)} can not handle {ipPacket.Protocol} packets!");

                    // extract tcpPacket
                    var tcpPacket = PacketUtil.ExtractTcp(ipPacket);
                    if (Equals(ipPacket.DestinationAddress, loopbackAddress))
                    {
                        // redirect to inbound
                        var natItem = (NatItemEx?)Client.Nat.Resolve(ipPacket.Version, ipPacket.Protocol, tcpPacket.DestinationPort);
                        if (natItem != null)
                        {
                            ipPacket.SourceAddress = natItem.DestinationAddress;
                            ipPacket.DestinationAddress = natItem.SourceAddress;
                            tcpPacket.SourcePort = natItem.DestinationPort;
                            tcpPacket.DestinationPort = natItem.SourcePort;
                        }
                        else
                        {
                            VhLogger.Instance.LogWarning(
                                $"Could not find incoming destination in NAT! Packet has been dropped. DesPort: {ipPacket.Protocol}:{tcpPacket.DestinationPort}");
                            ipPacket = PacketUtil.CreateTcpResetReply(ipPacket);
                        }
                    }
                    // Redirect outbound to the local address
                    else
                    {
                        var sync = tcpPacket.Synchronize && !tcpPacket.Acknowledgment;
                        var natItem = sync
                            ? Client.Nat.Add(ipPacket, true)
                            : Client.Nat.Get(ipPacket);

                        // could not find the tcp session natItem
                        if (natItem != null)
                        {
                            tcpPacket.SourcePort = natItem.NatId; // 1
                            ipPacket.DestinationAddress = ipPacket.SourceAddress; // 2
                            ipPacket.SourceAddress = loopbackAddress; //3
                            tcpPacket.DestinationPort = (ushort)localEndPoint.Port; //4
                        }
                        else
                        {
                            VhLogger.Instance.LogWarning(
                                $"Could not find outgoing tcp destination in NAT! Packet has been dropped. DesPort: {ipPacket.Protocol}:{tcpPacket.DestinationPort}");
                            ipPacket = PacketUtil.CreateTcpResetReply(ipPacket);
                        }
                    }

                    PacketUtil.UpdateIpPacket(ipPacket);
                    ret.Add(ipPacket);
                }
                catch (Exception ex)
                {
                    VhLogger.Instance.LogError(
                        $"{VhLogger.FormatTypeName(this)}: Error in processing packet! Error: {ex}");
                }
            }

            return ret.ToArray();
        }

        private async Task ProcessClient(TcpClient tcpOrgClient, CancellationToken cancellationToken)
        {
            if (tcpOrgClient is null) throw new ArgumentNullException(nameof(tcpOrgClient));
            TcpClientStream? tcpProxyClientStream = null;

            try
            {
                // config tcpOrgClient
                tcpOrgClient.NoDelay = true;
                Util.TcpClient_SetKeepAlive(tcpOrgClient, true);

                // get original remote from NAT
                var orgRemoteEndPoint = (IPEndPoint)tcpOrgClient.Client.RemoteEndPoint;
                var ipVersion = orgRemoteEndPoint.AddressFamily == AddressFamily.InterNetwork
                    ? IPVersion.IPv4
                    : IPVersion.IPv6;
                var natItem = (NatItemEx?)Client.Nat.Resolve(ipVersion, ProtocolType.Tcp, (ushort)orgRemoteEndPoint.Port);
                if (natItem == null)
                    throw new Exception(
                        $"Could not resolve original remote from NAT! RemoteEndPoint: {VhLogger.Format(tcpOrgClient.Client.RemoteEndPoint)}");

                // create a scope for the logger
                using var _ = VhLogger.Instance.BeginScope(
                    $"LocalPort: {natItem.SourcePort}, RemoteEp: {natItem.DestinationAddress}:{natItem.DestinationPort}");
                VhLogger.Instance.LogTrace(GeneralEventId.StreamChannel, "New TcpProxy Request.");

                // check invalid income
                var loopbackAddress = ipVersion == IPVersion.IPv4 ? LoopbackAddressIpV4 : LoopbackAddressIpV6;
                if (!Equals(orgRemoteEndPoint.Address, loopbackAddress))
                    throw new Exception("TcpProxy rejected an outbound connection!");

                // Check IpFilter
                if (!Client.IsInIpRange(natItem.DestinationAddress))
                {
                    await Client.AddPassthruTcpStream(
                        new TcpClientStream(tcpOrgClient, tcpOrgClient.GetStream()),
                        new IPEndPoint(natItem.DestinationAddress, natItem.DestinationPort),
                        cancellationToken);
                    return;
                }

                // Create the Request
                var request = new TcpProxyChannelRequest(
                    Client.SessionId,
                    Client.SessionKey,
                    new IPEndPoint(natItem.DestinationAddress, natItem.DestinationPort),
                    Util.GenerateSessionKey(),
                    natItem.DestinationPort == 443 ? TunnelUtil.TlsHandshakeLength : -1);

                tcpProxyClientStream =
                    await Client.GetSslConnectionToServer(GeneralEventId.StreamChannel, cancellationToken);
                tcpProxyClientStream.TcpClient.ReceiveBufferSize = tcpOrgClient.ReceiveBufferSize;
                tcpProxyClientStream.TcpClient.SendBufferSize = tcpOrgClient.SendBufferSize;
                tcpProxyClientStream.TcpClient.SendTimeout = tcpOrgClient.SendTimeout;
                tcpProxyClientStream.TcpClient.NoDelay = true;
                Util.TcpClient_SetKeepAlive(tcpProxyClientStream.TcpClient, true);

                // read the response
                await Client.SendRequest<ResponseBase>(tcpProxyClientStream.Stream,
                    RequestCode.TcpProxyChannel, request, cancellationToken);

                // create a TcpProxyChannel
                VhLogger.Instance.LogTrace(GeneralEventId.StreamChannel,
                    $"Adding a channel to session {VhLogger.FormatSessionId(request.SessionId)}...");
                var orgTcpClientStream = new TcpClientStream(tcpOrgClient, tcpOrgClient.GetStream());

                // Dispose ssl stream and replace it with a HeadCryptor
                await tcpProxyClientStream.Stream.DisposeAsync();
                tcpProxyClientStream.Stream = StreamHeadCryptor.Create(tcpProxyClientStream.TcpClient.GetStream(),
                    request.CipherKey, null, request.CipherLength);

                var channel = new TcpProxyChannel(orgTcpClientStream, tcpProxyClientStream);
                Client.Tunnel.AddChannel(channel);
            }
            catch (Exception ex)
            {
                tcpProxyClientStream?.Dispose();
                tcpOrgClient.Dispose();
                VhLogger.Instance.LogError(GeneralEventId.StreamChannel, $"{ex.Message}");
            }
        }
    }
}