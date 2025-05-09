using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Packets.VhPackets;
using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Collections;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Tunneling;

internal class UdpProxyEx : ITimeoutItem
{
    private readonly IPacketReceiver _packetReceiver;
    private readonly UdpClient _udpClient;
    private readonly SemaphoreSlim _sendSemaphore = new(1, 1);

    public TimeoutDictionary<IPEndPoint, TimeoutItem<IPEndPoint>> DestinationEndPointMap { get; }
    public DateTime LastUsedTime { get; set; }
    public AddressFamily AddressFamily { get; }
    public bool Disposed { get; private set; }
    public IPEndPoint LocalEndPoint { get; }

    public UdpProxyEx(IPacketReceiver packetReceiver, UdpClient udpClient, TimeSpan udpTimeout)
    {
        _packetReceiver = packetReceiver;
        _udpClient = udpClient;
        AddressFamily = udpClient.Client.AddressFamily;
        DestinationEndPointMap = new TimeoutDictionary<IPEndPoint, TimeoutItem<IPEndPoint>>(udpTimeout);
        LastUsedTime = FastDateTime.Now;
        LocalEndPoint = (IPEndPoint)udpClient.Client.LocalEndPoint;

        // prevent raise exception when there is no listener
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            udpClient.Client.IOControl(-1744830452, [0], [0]);
        }

        _ = Listen();
    }

    private bool IsInvalidState(Exception ex)
    {
        return Disposed || ex is ObjectDisposedException
            or SocketException { SocketErrorCode: SocketError.InvalidArgument };
    }

    public async Task SendPacket(IPEndPoint ipEndPoint, byte[] datagram, bool? noFragment)
    {
        LastUsedTime = FastDateTime.Now;

        try {
            await _sendSemaphore.WaitAsync().VhConfigureAwait();

            if (VhLogger.IsDiagnoseMode)
                VhLogger.Instance.LogTrace(GeneralEventId.Udp,
                    $"Sending all udp bytes to host. Requested: {datagram.Length}, From: {VhLogger.Format(LocalEndPoint)}, To: {VhLogger.Format(ipEndPoint)}");

            // IpV4 fragmentation
            if (noFragment != null && ipEndPoint.AddressFamily == AddressFamily.InterNetwork)
                _udpClient.DontFragment =
                    noFragment.Value; // Never call this for IPv6, it will throw exception for any value

            var sentBytes = await _udpClient.SendAsync(datagram, datagram.Length, ipEndPoint).VhConfigureAwait();
            if (sentBytes != datagram.Length)
                VhLogger.Instance.LogWarning(
                    $"Couldn't send all udp bytes. Requested: {datagram.Length}, Sent: {sentBytes}");
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(GeneralEventId.Udp,
                "Couldn't send a udp packet. RemoteEp: {RemoteEp}, Exception: {Message}",
                VhLogger.Format(ipEndPoint), ex.Message);

            if (IsInvalidState(ex))
                Dispose();
        }
        finally {
            _sendSemaphore.Release();
        }
    }

    public async Task Listen()
    {
        while (!Disposed) {
            var udpResult = await _udpClient.ReceiveAsync().VhConfigureAwait();
            LastUsedTime = FastDateTime.Now;

            // find the audience
            if (!DestinationEndPointMap.TryGetValue(udpResult.RemoteEndPoint, out var sourceEndPoint)) {
                VhLogger.Instance.LogInformation(GeneralEventId.Udp, "Could not find result UDP in the NAT!");
                return;
            }

            // create packet for audience
            var ipPacket = PacketBuilder.BuildUdp(
                udpResult.RemoteEndPoint.Address.GetAddressBytes(), sourceEndPoint.Value.Address.GetAddressBytes(),
                udpResult.RemoteEndPoint.Port, sourceEndPoint.Value.Port, udpResult.Buffer);

            ipPacket.UpdateAllChecksums();

            // send packet to audience
            _packetReceiver.OnPacketReceived(ipPacket);
        }
    }

    public void Dispose()
    {
        Disposed = true;
        _udpClient.Dispose();
    }
}