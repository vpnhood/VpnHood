using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.PacketTransports;
using VpnHood.Core.Toolkit.Collections;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Tunneling.Proxies;

internal class UdpProxy : SinglePacketTransport, ITimeoutItem
{
    private readonly UdpClient _udpClient;
    private IPEndPoint? _destinationEndPoint;
    private readonly byte[] _destinationAddressBytes = new byte[16];
    private int _destinationAddressLength;
    private bool? _dontFragment;
    public TimeoutDictionary<IPEndPoint, TimeoutItem<IPEndPoint>> DestinationEndPointMap { get; }
    public IPEndPoint LocalEndPoint { get; }
    public AddressFamily AddressFamily { get; }
    public DateTime LastUsedTime { get; set; }
    public new bool IsDisposed => base.IsDisposed;

    public UdpProxy(UdpClient udpClient, TimeSpan udpTimeout, int queueCapacity, bool autoDisposePackets)
        : base(new PacketTransportOptions {
            AutoDisposePackets = autoDisposePackets,
            Blocking = false,
            QueueCapacity = queueCapacity
        })
    {
        _udpClient = udpClient;
        DestinationEndPointMap = new TimeoutDictionary<IPEndPoint, TimeoutItem<IPEndPoint>>(udpTimeout);
        LastUsedTime = FastDateTime.Now;
        LocalEndPoint = udpClient.Client.GetLocalEndPoint();
        AddressFamily = LocalEndPoint.AddressFamily;

        // prevent raise exception when there is no listener
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            udpClient.Client.IOControl(-1744830452, [0], [0]);

        _ = StartReceivingAsync();
    }

    protected override async ValueTask SendPacketAsync(IpPacket ipPacket)
    {
        try {
            var udpPacket = ipPacket.ExtractUdp();

            // IpV4 fragmentation. DontFragment is a setsockopt syscall, so set it only when it changes.
            // Never call this for IPv6, it will throw exception for any value
            if (AddressFamily.IsV4() && ipPacket is IpV4Packet ipV4Packet && _dontFragment != ipV4Packet.DontFragment) {
                _udpClient.DontFragment = ipV4Packet.DontFragment;
                _dontFragment = ipV4Packet.DontFragment;
            }

            // check if the destination endpoint is changed via the address span,
            // so steady traffic allocates no IPAddress or IPEndPoint
            if (_destinationEndPoint == null ||
                _destinationEndPoint.Port != udpPacket.DestinationPort ||
                !ipPacket.DestinationAddressSpan.SequenceEqual(
                    _destinationAddressBytes.AsSpan(0, _destinationAddressLength))) {
                _destinationEndPoint = new IPEndPoint(ipPacket.DestinationAddress, udpPacket.DestinationPort);
                _destinationAddressLength = ipPacket.DestinationAddressSpan.Length;
                ipPacket.DestinationAddressSpan.CopyTo(_destinationAddressBytes);
            }

            // send packet to destination
            var sentBytes = await _udpClient.SendAsync(udpPacket.Payload, _destinationEndPoint).Vhc();
            LastUsedTime = FastDateTime.Now; // keep worker alive while receiving traffic

            if (sentBytes != udpPacket.Payload.Length)
                throw new Exception(
                    $"Couldn't send all udp bytes. Requested: {udpPacket.Payload.Length}, Sent: {sentBytes}");
        }
        catch (Exception ex) when (SocketUtils.IsInvalidUdpStateException(ex)) {
            VhLogger.Instance.LogError(ex, "Invalid UDP state detected in UdpProxy. Disposing the proxy.");
            Dispose();
            throw;
        }
    }

    private async Task StartReceivingAsync()
    {
        var consecutiveErrors = 0;
        while (!IsDisposed) {
            try {
                var udpResult = await _udpClient.ReceiveAsync().Vhc();
                consecutiveErrors = 0;

                // drop packets from unmapped remote endpoints (internet noise or a reply after the
                // mapping timed out) and keep receiving; other flows still rely on this shared socket.
                // Unmapped packets must not refresh LastUsedTime, or noise would pin idle workers
                if (!DestinationEndPointMap.TryGetValue(udpResult.RemoteEndPoint, out var sourceEndPoint)) {
                    if (VhLogger.MinLogLevel <= LogLevel.Debug)
                        VhLogger.Instance.LogDebug(GeneralEventId.Udp,
                            "Dropped a UDP packet from an unmapped remote endpoint. RemoteEndPoint: {RemoteEndPoint}",
                            VhLogger.Format(udpResult.RemoteEndPoint));
                    continue;
                }

                LastUsedTime = FastDateTime.Now; // keep worker alive while receiving traffic
                var ipPacket = PacketBuilder.BuildUdp(udpResult.RemoteEndPoint, sourceEndPoint.Value, udpResult.Buffer);
                ipPacket.UpdateAllChecksums();
                OnPacketReceived(ipPacket);
            }
            catch (Exception) when (IsDisposed) {
                break;
            }
            catch (Exception ex) when (SocketUtils.IsInvalidUdpStateException(ex)) {
                VhLogger.Instance.LogError(ex, "Unexpected error in UDP receive loop.");
                Dispose();
                break;
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "Error in UdpProxy receive loop.");

                // a persistent synchronous error must not hot-spin the shared the receive loop
                if (++consecutiveErrors >= 10)
                    await Task.Delay(1000).Vhc();
            }
        }
    }

    protected override void DisposeManaged()
    {
        DestinationEndPointMap.Dispose();
        _udpClient.Dispose();

        base.DisposeManaged();
    }
}
