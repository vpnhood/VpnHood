using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Packets;
using VpnHood.Core.PacketTransports;
using VpnHood.Core.Toolkit.Collections;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Tunneling.Proxies;

internal class UdpProxy : SinglePacketTransport, ITimeoutItem
{
    private readonly UdpClient _udpClient;
    private readonly IPEndPoint? _sourceEndPoint;
    public IPEndPoint LocalEndPoint { get; }
    public AddressFamily AddressFamily { get; }
    public DateTime LastUsedTime { get; set; }
    public new bool IsDisposed => base.IsDisposed;

    public UdpProxy(UdpClient udpClient, IPEndPoint? sourceEndPoint, int queueCapacity, bool autoDisposePackets)
        : base(new PacketTransportOptions {
            AutoDisposePackets = autoDisposePackets,
            Blocking = false,
            QueueCapacity = queueCapacity,
        })
    {
        _udpClient = udpClient;
        _sourceEndPoint = sourceEndPoint;
        LastUsedTime = FastDateTime.Now;
        LocalEndPoint = (IPEndPoint)udpClient.Client.LocalEndPoint;
        AddressFamily = LocalEndPoint.AddressFamily;

        // prevent raise exception when there is no listener
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            udpClient.Client.IOControl(-1744830452, [0], [0]);

        _ = StartReceivingAsync();
    }

    protected virtual IPEndPoint? GetSourceEndPoint(IPEndPoint remoteEndPoint)
    {
        return _sourceEndPoint;
    }

    private bool IsInvalidState(Exception ex)
    {
        return IsDisposed || ex is ObjectDisposedException
            or SocketException { SocketErrorCode: SocketError.InvalidArgument }
            or SocketException { SocketErrorCode: SocketError.ConnectionAborted }
            or SocketException { SocketErrorCode: SocketError.OperationAborted };
    }

    private readonly byte[] _payloadBuffer = new byte[1500]; //todo: Use SendAsync(Memory<>) later
    private IPEndPoint? _destinationEndPoint;
    protected override async ValueTask SendPacketAsync(IpPacket ipPacket)
    {
        try {
            // IpV4 fragmentation
            if (ipPacket.Protocol == IpProtocol.IPv4 && ipPacket is IpV4Packet ipV4Packet && AddressFamily.IsV4())
                _udpClient.DontFragment = ipV4Packet.DontFragment; // Never call this for IPv6, it will throw exception for any value

            var udpPacket = ipPacket.ExtractUdp();

            // check if the destination endpoint is changed. Prevent reallocating the buffer
            if (_destinationEndPoint == null || !_destinationEndPoint.Address.Equals(ipPacket.DestinationAddress) || _destinationEndPoint.Port != udpPacket.DestinationPort)
                _destinationEndPoint = new IPEndPoint(ipPacket.DestinationAddress, udpPacket.DestinationPort);

            // check if the payload is too large for the buffer
            if (udpPacket.Payload.Length > _payloadBuffer.Length)
                throw new InvalidOperationException("UDP payload too large for buffer.");
            udpPacket.Payload.CopyTo(_payloadBuffer);

            // send packet to destination
            var sentBytes = await _udpClient.SendAsync(_payloadBuffer, udpPacket.Payload.Length, _destinationEndPoint)
                .VhConfigureAwait();

            if (sentBytes != udpPacket.Payload.Length)
                throw new Exception(
                    $"Couldn't send all udp bytes. Requested: {udpPacket.Payload.Length}, Sent: {sentBytes}");
        }
        catch (Exception ex) {
            if (IsInvalidState(ex))
                Dispose();

            throw;
        }
    }

    private async Task StartReceivingAsync()
    {
        try {
            while (!IsDisposed) {
                var udpResult = await _udpClient.ReceiveAsync().VhConfigureAwait();

                // find the audience (sourceEndPoint)
                var sourceEndPoint = GetSourceEndPoint(udpResult.RemoteEndPoint);
                if (sourceEndPoint == null) {
                    VhLogger.Instance.LogInformation(GeneralEventId.Udp, "Could not find UDP source address.");
                    return;
                }

                var ipPacket = PacketBuilder.BuildUdp(udpResult.RemoteEndPoint, sourceEndPoint, udpResult.Buffer);
                ipPacket.UpdateAllChecksums();
                OnPacketReceived(ipPacket);
            }
        }
        catch (Exception ex) {
            if (!IsInvalidState(ex))
                VhLogger.Instance.LogError(ex, "Unexpected error in UDP receive loop.");
            
            Dispose();
        }
    }

    protected override void DisposeManaged()
    {
        _udpClient.Dispose();

        base.DisposeManaged();
    }
}