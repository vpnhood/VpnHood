using System;
using System.Net.NetworkInformation;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using PacketDotNet.Utils;
using VpnHood.Common.Logging;

namespace VpnHood.Tunneling
{
    public class PingProxy : IDisposable
    {
        private readonly Ping _ping;
        private readonly TimeSpan _timeout = TimeSpan.FromSeconds(6);

        /// <param name="ping">Will be disposed by this object</param>
        public PingProxy(Ping ping)
        {
            _ping = ping;
            _ping.PingCompleted += Ping_PingCompleted;
        }

        public void Dispose()
        {
            _ping.PingCompleted -= Ping_PingCompleted;
            _ping.Dispose();
        }

        public event EventHandler<PacketReceivedEventArgs>? OnPacketReceived;

        private void Ping_PingCompleted(object sender, PingCompletedEventArgs e)
        {
            try
            {
                if (e.Cancelled)
                {
                    VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.Ping, "Ping has been cancelled.");
                    return;
                }

                if (e.Error != null)
                {
                    VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.Ping,
                        $"Ping has been failed. Message: {e.Error.Message}");
                    return;
                }

                var pingReply = e.Reply ?? throw new Exception("Ping Reply is null!.");
                var ipPacket = (IPPacket)e.UserState ?? throw new Exception("UserState is null!");
                if (pingReply.Status != IPStatus.Success)
                {
                    if (VhLogger.IsDiagnoseMode)
                        VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.Ping,
                            $"Ping Reply has been failed! DestAddress: {pingReply.Address}, DataLen: {pingReply.Buffer.Length}, Data: {BitConverter.ToString(pingReply.Buffer, 0, Math.Min(10, pingReply.Buffer.Length))}.");
                    return;
                }

                // create the echoReply
                if (ipPacket.Version == IPVersion.IPv4)
                {
                    var icmpPacket = PacketUtil.ExtractIcmp(ipPacket);
                    icmpPacket.TypeCode = IcmpV4TypeCode.EchoReply;
                    icmpPacket.Data = pingReply.Buffer;
                }
                else
                {
                    // IcmpV6 packet generation is not fully implemented by packetdot net
                    // So create all packet in buffer
                    var icmpPacket = PacketUtil.ExtractIcmpV6(ipPacket);
                    icmpPacket.Type = IcmpV6Type.EchoReply;
                    icmpPacket.Code = 0;
                    var buffer = new byte[pingReply.Buffer.Length + 8];
                    Array.Copy(icmpPacket.Bytes, 0, buffer, 0, 8);
                    Array.Copy(pingReply.Buffer, 0, buffer, 8, pingReply.Buffer.Length);
                    icmpPacket = new IcmpV6Packet(new ByteArraySegment(buffer));
                }

                ipPacket.DestinationAddress = ipPacket.SourceAddress;
                ipPacket.SourceAddress = pingReply.Address;
                PacketUtil.UpdateIpPacket(ipPacket);

                OnPacketReceived?.Invoke(this, new PacketReceivedEventArgs(ipPacket));
                if (VhLogger.IsDiagnoseMode)
                    VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.Ping,
                        $"Ping Reply has been delegated! DestAddress: {ipPacket.DestinationAddress}, DataLen: {pingReply.Buffer.Length}, Data: {BitConverter.ToString(pingReply.Buffer, 0, Math.Min(10, pingReply.Buffer.Length))}.");
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogError($"Unexpected exception has been occurred! Message: {ex}.");
            }
        }

        public void Send(IPPacket ipPacket)
        {
            if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));
            if (ipPacket.Version == IPVersion.IPv4)
                SendIpV4(ipPacket.Extract<IPv4Packet>());
            else
                SendIpV6(ipPacket.Extract<IPv6Packet>());
        }

        private void SendIpV4(IPv4Packet ipPacket)
        {
            if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));
            if (ipPacket.Protocol != ProtocolType.Icmp)
                throw new ArgumentException($"Packet is not {ProtocolType.Icmp}!", nameof(ipPacket));

            // We should not use Task due its stack usage, this method is called by many session each many times!
            var icmpPacket = PacketUtil.ExtractIcmp(ipPacket);
            var noFragment = (ipPacket.FragmentFlags & 0x2) != 0;
            var pingOptions = new PingOptions(ipPacket.TimeToLive - 1, noFragment);
            _ping.SendAsync(ipPacket.DestinationAddress, (int)_timeout.TotalMilliseconds, icmpPacket.Data, pingOptions, ipPacket);

            if (VhLogger.IsDiagnoseMode)
            {
                var buf = icmpPacket.Data ?? Array.Empty<byte>();
                VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.Ping,
                    $"PingV4 Send has been delegated! DestAddress: {ipPacket.DestinationAddress}, DataLen: {buf}, Data: {BitConverter.ToString(buf, 0, Math.Min(10, buf.Length))}.");
            }
        }

        private void SendIpV6(IPv6Packet ipPacket)
        {
            if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));
            if (ipPacket.Protocol != ProtocolType.IcmpV6)
                throw new ArgumentException($"Packet is not {ProtocolType.IcmpV6}!", nameof(ipPacket));

            // We should not use Task due its stack usage, this method is called by many session each many times!
            var icmpPacket = PacketUtil.ExtractIcmpV6(ipPacket);
            var pingOptions = new PingOptions(ipPacket.TimeToLive - 1, true);
            var pingData = icmpPacket.Bytes[8..];
            _ping.SendAsync(ipPacket.DestinationAddress, (int)_timeout.TotalMilliseconds, pingData, pingOptions, ipPacket);

            if (VhLogger.IsDiagnoseMode)
            {
                var buf = pingData ?? Array.Empty<byte>();
                VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.Ping,
                    $"Ping Send has been delegated! DestAddress: {ipPacket.DestinationAddress}, DataLen: {buf}, Data: {BitConverter.ToString(buf, 0, Math.Min(10, buf.Length))}.");
            }
        }
    }
}