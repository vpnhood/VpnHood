using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Common.Logging;
using ProtocolType = PacketDotNet.ProtocolType;

namespace VpnHood.Tunneling
{
    internal class UdpProxy : IDisposable
    {
        private readonly IPEndPoint _sourceEndPoint;
        private readonly UdpClient _udpClient;
        private IPEndPoint? _lastHostEndPoint;
        private IPPacket? _lastPacket;
        private bool _sameHost = true;

        /// <param name="udpClientListener">Will be disposed by this object</param>
        /// <param name="sourceEndPoint"></param>
        public UdpProxy(UdpClient udpClientListener, IPEndPoint sourceEndPoint)
        {
            _udpClient = udpClientListener ?? throw new ArgumentNullException(nameof(udpClientListener));
            _sourceEndPoint = sourceEndPoint ?? throw new ArgumentNullException(nameof(sourceEndPoint));
            using var scope = VhLogger.Instance.BeginScope($"{VhLogger.FormatTypeName<UdpProxy>()}, LocalPort: {LocalPort}");
            VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.Udp,
                $"A UdpProxy has been created. LocalEp: {_udpClient.Client.LocalEndPoint}");
            _udpClient.EnableBroadcast = true;
            _ = ReceiveUdpTask();
        }

        public bool IsDisposed { get; private set; }

        public int LocalPort => (ushort)((IPEndPoint)_udpClient.Client.LocalEndPoint).Port;

        public void Dispose()
        {
            if (IsDisposed)
                return;
            IsDisposed = true;

            _udpClient.Dispose();
        }


        public event EventHandler<PacketReceivedEventArgs>? OnPacketReceived;

        private async Task ReceiveUdpTask()
        {
            var localEndPoint = (IPEndPoint)_udpClient.Client.LocalEndPoint;

            using var _ = VhLogger.Instance.BeginScope($"UdpProxy LocalEp: {localEndPoint}");
            VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.Udp, "Start listening...");

            while (!IsDisposed)
                try
                {
                    //receiving packet
                    var udpResult = await _udpClient.ReceiveAsync();

                    // forward packet
                    var ipPacket = PacketUtil.CreateIpPacket(udpResult.RemoteEndPoint.Address, _sourceEndPoint.Address);
                    var udpPacket = new UdpPacket((ushort)udpResult.RemoteEndPoint.Port, (ushort)_sourceEndPoint.Port)
                    {
                        PayloadData = udpResult.Buffer
                    };
                    ipPacket.PayloadPacket = udpPacket;
                    PacketUtil.UpdateIpPacket(ipPacket);

                    OnPacketReceived?.Invoke(this, new PacketReceivedEventArgs(ipPacket));
                }
                // delegate connection reset
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
                {
                    if (_sameHost && _lastPacket != null)
                    {
                        var replyPacket =
                            PacketUtil.CreateUnreachablePortReply(_lastPacket);
                        OnPacketReceived?.Invoke(this, new PacketReceivedEventArgs(replyPacket));
                        if (VhLogger.IsDiagnoseMode && !IsDisposed)
                            VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.Udp,
                                $"{VhLogger.FormatTypeName(this)} delegate a connection reset from {_lastHostEndPoint}!");
                    }
                    else
                    {
                        // show error if session is not disposed yet
                        if (VhLogger.IsDiagnoseMode && !IsDisposed)
                            VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.Udp,
                                $"{VhLogger.FormatTypeName(this)} received error! Error: {ex.Message}");
                    }
                }
                // ignore exception and listen for next packets
                catch (Exception ex)
                {
                    // show error if session is not disposed yet
                    if (VhLogger.IsDiagnoseMode && !IsDisposed)
                        VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.Udp,
                            $"{VhLogger.FormatTypeName(this)} received error! Error: {ex.Message}");

                    if (IsInvalidState(ex))
                        Dispose();
                }

            // show error if session is not disposed yet
            VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.Udp,
                $"{VhLogger.FormatTypeName(this)} listener has been stopped!");
        }

        public void Send(IPPacket ipPacket)
        {
            if (ipPacket == null) throw new ArgumentNullException(nameof(ipPacket));
            if (ipPacket.Protocol != ProtocolType.Udp)
                throw new ArgumentException($"Packet is not {ProtocolType.Udp}!", nameof(ipPacket));

            var udpPacket = PacketUtil.ExtractUdp(ipPacket);
            var dgram = udpPacket.PayloadData ?? Array.Empty<byte>();

            var ipEndPoint = new IPEndPoint(ipPacket.DestinationAddress, udpPacket.DestinationPort);
            _udpClient.DontFragment = ipPacket is IPv4Packet ipV4Packet && (ipV4Packet.FragmentFlags & 0x2) != 0 ||
                                      ipPacket is IPv6Packet;
            _sameHost = _sameHost && (_lastHostEndPoint == null || _lastHostEndPoint.Equals(ipEndPoint));

            // save last endpoint
            _lastHostEndPoint = ipEndPoint;
            _lastPacket = ipPacket;

            try
            {
                if (VhLogger.IsDiagnoseMode)
                    VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.Udp,
                        $"Sending all udp bytes to host. Requested: {dgram.Length}, From: {_udpClient.Client.LocalEndPoint}, To: {ipEndPoint}");

                var sentBytes = _udpClient.Send(dgram, dgram.Length, ipEndPoint);
                if (sentBytes != dgram.Length)
                    VhLogger.Instance.LogWarning(
                        $"Couldn't send all udp bytes. Requested: {dgram.Length}, Sent: {sentBytes}");
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogWarning(
                    $"Couldn't send a udp packet to {VhLogger.Format(ipEndPoint)}. Error: {ex.Message}");
                if (IsInvalidState(ex))
                    Dispose();
            }
        }

        private bool IsInvalidState(Exception ex)
        {
            return IsDisposed || ex is ObjectDisposedException 
                or SocketException {SocketErrorCode: SocketError.InvalidArgument};
        }
    }
}