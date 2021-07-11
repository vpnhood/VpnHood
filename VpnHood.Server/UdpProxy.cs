using VpnHood.Server.Factory;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using System;
using System.Net;
using System.Net.Sockets;
using VpnHood.Logging;
using VpnHood.Tunneling;
using System.Threading.Tasks;

namespace VpnHood.Server
{
    class UdpProxy : IDisposable
    {
        private readonly UdpClient _udpClient;
        private readonly IPEndPoint _sourceEndPoint;
        private IPPacket _lastPacket;
        private IPEndPoint _lastHostEndPoint;
        private bool _sameHost = true;
        public bool IsDisposed { get; private set; }

        public int LocalPort => (ushort)((IPEndPoint)_udpClient.Client.LocalEndPoint).Port;


        public event EventHandler<PacketReceivedEventArgs> OnPacketReceived;

        /// <param name="udpClientListener">Will be disposed by this object</param>
        public UdpProxy(UdpClient udpClientListener, IPEndPoint sourceEndPoint)
        {
            if (udpClientListener is null) throw new ArgumentNullException(nameof(udpClientListener));
            if (sourceEndPoint is null) throw new ArgumentNullException(nameof(sourceEndPoint));

            _udpClient = udpClientListener;
            _sourceEndPoint = sourceEndPoint;
            using var _ = VhLogger.Instance.BeginScope($"{VhLogger.FormatTypeName<UdpProxy>()}, LocalPort: {LocalPort}");
            VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.Udp, $"A UdpProxy has been created. LocalEp: {_udpClient.Client.LocalEndPoint}");
            _udpClient.EnableBroadcast = true;
            var udpTask = ReceiveUdpTask();
        }

        private async Task ReceiveUdpTask()
        {
            var udpClient = _udpClient;
            var localEndPoint = (IPEndPoint)udpClient.Client.LocalEndPoint;

            using var _ = VhLogger.Instance.BeginScope($"UdpProxy LocalEp: {localEndPoint}");
            VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.Udp, $"Start listening...");

            while (!IsDisposed)
            {
                try
                {
                    //receiving packet
                    var udpResult = await udpClient.ReceiveAsync();

                    // forward packet
                    var ipPacket = new IPv4Packet(udpResult.RemoteEndPoint.Address, _sourceEndPoint.Address);
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
                        var replyPacket = PacketUtil.CreateUnreachableReply(_lastPacket, IcmpV4TypeCode.UnreachablePort);
                        OnPacketReceived?.Invoke(this, new PacketReceivedEventArgs(replyPacket));
                        if (VhLogger.IsDiagnoseMode && !IsDisposed)
                            VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.Udp, $"{VhLogger.FormatTypeName(this)} delegate a connection reset from {_lastHostEndPoint}!");
                    }
                    else
                    {
                        // show error if session is not disposed yet
                        if (VhLogger.IsDiagnoseMode && !IsDisposed)
                            VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.Udp, $"{VhLogger.FormatTypeName(this)} received error! Error: {ex.Message}");
                    }
                }
                // ignore exception and listen for next packets
                catch (Exception ex)
                {
                    // show error if session is not disposed yet
                    if (VhLogger.IsDiagnoseMode && !IsDisposed)
                        VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.Udp, $"{VhLogger.FormatTypeName(this)} received error! Error: {ex.Message}");

                    if (IsInvalidState(ex))
                        Dispose();
                }
            }

            // show error if session is not disposed yet
            VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.Udp, $"{VhLogger.FormatTypeName(this)} listener has been stopped!");
        }

        public void Send(IPPacket ipPacket)
        {
            if (ipPacket == null) throw new ArgumentNullException(nameof(ipPacket));
            if (ipPacket.Protocol != PacketDotNet.ProtocolType.Udp) throw new ArgumentException($"Packet is not {PacketDotNet.ProtocolType.Udp}!", nameof(ipPacket));

            var udpPacket = PacketUtil.ExtractUdp(ipPacket);
            var dgram = udpPacket.PayloadData ?? Array.Empty<byte>();

            var ipEndPoint = new IPEndPoint(ipPacket.DestinationAddress, udpPacket.DestinationPort);
            _udpClient.DontFragment = ((ipPacket is IPv4Packet ipV4Packet) && (ipV4Packet.FragmentFlags & 0x2) != 0) || ipPacket is IPv6Packet;
            _sameHost = _sameHost && _lastHostEndPoint == null || _lastHostEndPoint.Equals(ipEndPoint);

            // save last endpoint
            _lastHostEndPoint = ipEndPoint;
            _lastPacket = ipPacket;

            try
            {
                if (VhLogger.IsDiagnoseMode)
                    VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.Udp, $"Sending all udp bytes to host. Requested: {dgram.Length}, From: {_udpClient.Client.LocalEndPoint}, To: {ipEndPoint}");

                var sentBytes = _udpClient.Send(dgram, dgram.Length, ipEndPoint);
                if (sentBytes != dgram.Length)
                    VhLogger.Instance.LogWarning($"Couldn't send all udp bytes. Requested: {dgram.Length}, Sent: {sentBytes}");
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogWarning($"Couldn't send a udp packet to {VhLogger.Format(ipEndPoint)}. Error: {ex.Message}");
                if (IsInvalidState(ex))
                    Dispose();
            }
        }

        private bool IsInvalidState(Exception ex) =>
            IsDisposed ||
            (ex is ObjectDisposedException ||
            (ex is SocketException socketException && socketException.SocketErrorCode == SocketError.InvalidArgument));

        public void Dispose()
        {
            if (IsDisposed)
                return;
            IsDisposed = true;

            _udpClient.Dispose();
        }
    }
}

