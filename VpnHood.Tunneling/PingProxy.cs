using System;
using System.Net.NetworkInformation;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Common.Logging;

namespace VpnHood.Tunneling
{
    public class PingProxy : IDisposable
    {
        private readonly Ping _ping;
        private readonly int _timeout = 6000;

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
                var ipPacket = (IPv4Packet) e.UserState ?? throw new Exception("UserState is null!");
                if (pingReply.Status != IPStatus.Success)
                {
                    if (VhLogger.IsDiagnoseMode)
                        VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.Ping,
                            $"Ping Reply has been failed! DestAddress: {pingReply.Address}, DataLen: {pingReply.Buffer.Length}, Data: {BitConverter.ToString(pingReply.Buffer, 0, Math.Min(10, pingReply.Buffer.Length))}.");
                    return;
                }

                // create the echoReply
                var icmpPacket = PacketUtil.ExtractIcmp(ipPacket);
                icmpPacket.TypeCode = IcmpV4TypeCode.EchoReply;
                icmpPacket.Data = pingReply.Buffer;
                ipPacket.DestinationAddress = ipPacket.SourceAddress;
                ipPacket.SourceAddress = pingReply.Address;
                PacketUtil.UpdateIpPacket(ipPacket);

                OnPacketReceived?.Invoke(this, new PacketReceivedEventArgs(ipPacket));
                if (VhLogger.IsDiagnoseMode)
                    VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.Ping,
                        $"Ping Reply has been delegated! DestAddress: {ipPacket?.DestinationAddress}, DataLen: {pingReply.Buffer.Length}, Data: {BitConverter.ToString(pingReply.Buffer, 0, Math.Min(10, pingReply.Buffer.Length))}.");
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogError($"Unexpected exception has been occurred! Message: {ex}.");
            }
        }

        public void Send(IPPacket ipPacket)
        {
            if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));
            if (ipPacket.Protocol != ProtocolType.Icmp)
                throw new ArgumentException($"Packet is not {ProtocolType.Icmp}!", nameof(ipPacket));

            // We should not use Task due its stack usage, this method is called by many session each many times!
            var icmpPacket = PacketUtil.ExtractIcmp(ipPacket);
            var dontFragment = ipPacket is IPv4Packet ipV4Packet && (ipV4Packet.FragmentFlags & 0x2) != 0 ||
                               ipPacket is IPv6Packet;
            var pingOptions = new PingOptions(ipPacket.TimeToLive - 1, dontFragment);
            _ping.SendAsync(ipPacket.DestinationAddress, _timeout, icmpPacket.Data, pingOptions, ipPacket);

            if (VhLogger.IsDiagnoseMode)
            {
                var buf = icmpPacket.Data ?? new byte[0];
                VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.Ping,
                    $"Ping Send has been delegated! DestAddress: {ipPacket?.DestinationAddress}, DataLen: {buf}, Data: {BitConverter.ToString(buf, 0, Math.Min(10, buf.Length))}.");
            }
        }
    }
}