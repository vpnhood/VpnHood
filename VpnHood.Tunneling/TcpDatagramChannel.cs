using Microsoft.Extensions.Logging;
using PacketDotNet;
using System;
using System.Linq;
using System.Net;
using System.Threading;
using VpnHood.Logging;

namespace VpnHood.Tunneling
{
    public class TcpDatagramChannel : IDatagramChannel
    {
        private TcpClientStream _tcpClientStream;
        private Thread _thread;

        public event EventHandler OnFinished;
        public event EventHandler<ChannelPacketArrivalEventArgs> OnPacketArrival;
        public int SendBufferSize => _tcpClientStream.TcpClient.SendBufferSize;
        public bool Connected { get; private set; }
        public long SentByteCount { get; private set; }
        public long ReceivedByteCount { get; private set; }

        public TcpDatagramChannel(TcpClientStream tcpClientStream)
        {
            _tcpClientStream = tcpClientStream ?? throw new ArgumentNullException(nameof(tcpClientStream));
            tcpClientStream.TcpClient.NoDelay = true;
        }

        public void Start()
        {
            if (_thread != null)
                throw new Exception("Start has already been called!");

            if (_disposed)
                throw new ObjectDisposedException(nameof(TcpDatagramChannel));

            Connected = true;
            _thread = new Thread(ReadThread, TunnelUtil.SocketStackSize_Stream); //256K
            _thread.Start();
        }

        private void ReadThread(object obj)
        {
            var tcpClient = _tcpClientStream.TcpClient;
            var stream = _tcpClientStream.Stream;

            try
            {
                while (tcpClient.Connected)
                {
                    var ipPacket = TunnelUtil.Stream_ReadIpPacket(stream);
                    if (ipPacket == null || _disposed)
                        break;

                    ReceivedByteCount += ipPacket.TotalPacketLength;
                    OnPacketArrival?.Invoke(this, new ChannelPacketArrivalEventArgs(ipPacket, this));
                }
            }
            catch
            {
            }
            finally
            {
                Connected = false;
                if (!_disposed)
                    OnFinished?.Invoke(this, EventArgs.Empty);
            }
        }

        public void SendPacket(IPPacket[] ipPackets)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TcpDatagramChannel));

            var size = ipPackets.Sum(packet => packet.TotalPacketLength);
            var buffer = new byte[size];
            var destIndex = 0;
            foreach (var ipPacket in ipPackets)
            {
                // log ICMP
                if (VhLogger.IsDiagnoseMode && ipPacket.Protocol == ProtocolType.Icmp)
                {
                    var icmpPacket = ipPacket.Extract<IcmpV4Packet>();
                    VhLogger.Current.Log(LogLevel.Information, GeneralEventId.Ping, $"Sending an ICMP to a channel. DestAddress: {ipPacket.DestinationAddress}, DataLen: {icmpPacket.Data.Length}, Data: {BitConverter.ToString(icmpPacket?.Data, 0, Math.Min(10, icmpPacket.Data.Length))}.");
                }

                if (VhLogger.IsDiagnoseMode && ipPacket.Protocol == ProtocolType.Udp)
                {
                    var udp = ipPacket.Extract<UdpPacket>();
                    VhLogger.Current.Log(LogLevel.Information, GeneralEventId.Udp, $"Sending an UDP to a channel. DestAddress: {ipPacket.DestinationAddress}:{udp.DestinationPort}, DataLen: {udp.PayloadData.Length}, Data: {BitConverter.ToString(udp.PayloadData, 0, Math.Min(10, udp.PayloadData.Length))}.");
                }

                var source = ipPacket.Bytes;
                Buffer.BlockCopy(source, 0, buffer, destIndex, source.Length);
                destIndex += source.Length;
            }

            _tcpClientStream.Stream.Write(buffer, 0, buffer.Length);
            SentByteCount += buffer.Length;
        }

        private bool _disposed = false;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Connected = false;
            _tcpClientStream.Dispose();
            _tcpClientStream = null;
        }
    }
}
