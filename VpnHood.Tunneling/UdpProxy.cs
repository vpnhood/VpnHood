using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Collections;
using VpnHood.Common.Logging;
using VpnHood.Common.Utils;

namespace VpnHood.Tunneling;

internal class UdpProxy : ITimeoutItem
{
    private readonly IPacketReceiver _packetReceiver;
    private readonly UdpClient _udpClient;
    private readonly SemaphoreSlim _sendSemaphore = new(1, 1);

    public DateTime LastUsedTime { get; set; }
    public IPEndPoint SourceEndPoint { get; }
    public bool Disposed { get; private set; }
    public IPEndPoint LocalEndPoint { get; }

    public UdpProxy(IPacketReceiver packetReceiver, UdpClient udpClient, IPEndPoint sourceEndPoint)
    {
        _packetReceiver = packetReceiver;
        _udpClient = udpClient;
        SourceEndPoint = sourceEndPoint;
        LastUsedTime = FastDateTime.Now;
        LocalEndPoint = (IPEndPoint)udpClient.Client.LocalEndPoint;

        // prevent raise exception when there is no listener
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            udpClient.Client.IOControl(-1744830452, new byte[] { 0 }, new byte[] { 0 });

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

        try
        {
            await _sendSemaphore.WaitAsync();

            if (VhLogger.IsDiagnoseMode)
                VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.Udp,
                    $"Sending all udp bytes to host. Requested: {datagram.Length}, From: {VhLogger.Format(LocalEndPoint)}, To: {VhLogger.Format(ipEndPoint)}");

            // IpV4 fragmentation
            if (noFragment != null && ipEndPoint.AddressFamily == AddressFamily.InterNetwork)
                _udpClient.DontFragment = noFragment.Value; // Never call this for IPv6, it will throw exception for any value

            var sentBytes = await _udpClient.SendAsync(datagram, datagram.Length, ipEndPoint);
            if (sentBytes != datagram.Length)
                VhLogger.Instance.LogWarning(
                    $"Couldn't send all udp bytes. Requested: {datagram.Length}, Sent: {sentBytes}");
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogWarning(
                $"Couldn't send a udp packet to {VhLogger.Format(ipEndPoint)}. Error: {ex.Message}");

            if (IsInvalidState(ex))
                Dispose();
        }
        finally
        {
            _sendSemaphore.Release();
        }
    }

    public async Task Listen()
    {
        while (!Disposed)
        {
            var udpResult = await _udpClient.ReceiveAsync();
            LastUsedTime = FastDateTime.Now;

            // create packet for audience
            var ipPacket = PacketUtil.CreateUdpPacket(udpResult.RemoteEndPoint, SourceEndPoint, udpResult.Buffer);

            // send packet to audience
            await _packetReceiver.OnPacketReceived(ipPacket);
        }
    }

    public void Dispose()
    {
        Disposed = true;
        _udpClient.Dispose();
    }
}