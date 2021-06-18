using VpnHood.Server.Factory;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using VpnHood.Logging;
using VpnHood.Tunneling;

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

        public UdpProxy(UdpClientFactory udpClientFactory, IPEndPoint sourceEndPoint)
        {
            if (udpClientFactory is null) throw new ArgumentNullException(nameof(udpClientFactory));
            if (sourceEndPoint is null) throw new ArgumentNullException(nameof(sourceEndPoint));

            _udpClient = udpClientFactory.CreateListner();
            _sourceEndPoint = sourceEndPoint;
            using var _ = VhLogger.Instance.BeginScope($"{VhLogger.FormatTypeName<UdpProxy>()}, LocalPort: {LocalPort}");
            VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.Udp, $"A UdpProxy has been created. LocalEp: {_udpClient.Client.LocalEndPoint}");
            _udpClient.EnableBroadcast = true;
            var thread = new Thread(ReceiveUdpThread, TunnelUtil.SocketStackSize_Datagram);
            thread.Start();
        }

        private void ReceiveUdpThread(object obj)
        {
            var udpClient = _udpClient;
            var localEndPoint = (IPEndPoint)udpClient.Client.LocalEndPoint;

            using var _ = VhLogger.Instance.BeginScope($"UdpProxy LocalEp: {localEndPoint}");
            VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.Udp, $"Start listening...");

            IPEndPoint remoteEp = null;
            while (!IsDisposed)
            {
                try
                {
                    //receiving packet
                    var udpResult = udpClient.Receive(ref remoteEp);

                    // forward packet
                    var ipPacket = new IPv4Packet(remoteEp.Address, _sourceEndPoint.Address);
                    var udpPacket = new UdpPacket((ushort)remoteEp.Port, (ushort)_sourceEndPoint.Port)
                    {
                        PayloadData = udpResult
                    };
                    ipPacket.PayloadPacket = udpPacket;
                    udpPacket.UpdateUdpChecksum();
                    udpPacket.UpdateCalculatedValues();
                    ipPacket.UpdateIPChecksum();
                    ipPacket.UpdateCalculatedValues();

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

        public void Send(IPv4Packet ipPacket)
        {
            var udpPacket = ipPacket.Extract<UdpPacket>();
            var dgram = udpPacket.PayloadData ?? Array.Empty<byte>();

            var ipEndPoint = new IPEndPoint(ipPacket.DestinationAddress, udpPacket.DestinationPort);
            _udpClient.DontFragment = (ipPacket.FragmentFlags & 0x2) != 0;
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

