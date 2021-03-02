using Microsoft.Extensions.Logging;
using PacketDotNet;
using System;
using System.Net.NetworkInformation;
using VpnHood.Logging;
using VpnHood.Tunneling;

namespace VpnHood.Server
{
    public class PingProxy : IDisposable
    {
        private readonly int _timeout = 6000;
        private readonly Ping _ping;
        public event EventHandler<PingCompletedEventArgs> OnPingCompleted;

        public PingProxy()
        {
            _ping = new Ping();
            _ping.PingCompleted += Ping_PingCompleted;
        }

        private void Ping_PingCompleted(object sender, System.Net.NetworkInformation.PingCompletedEventArgs e)
        {
            try
            {

                if (e.Cancelled)
                {
                    VhLogger.Current.Log(LogLevel.Information, GeneralEventId.Ping, $"Ping has been cancelled.");
                    return;
                }

                if (e.Error != null)
                {
                    VhLogger.Current.Log(LogLevel.Information, GeneralEventId.Ping, $"Ping has been failed. Message: {e.Error.Message}");
                    return;
                }

                var pingReply = e.Reply;
                var ipPacket = (IPv4Packet)e.UserState;
                if (pingReply?.Status != IPStatus.Success)
                {
                    if (VhLogger.IsDiagnoseMode)
                        VhLogger.Current.Log(LogLevel.Information, GeneralEventId.Ping, $"Ping Reply has been failed! DestAddress: {pingReply?.Address}, DataLen: {pingReply.Buffer.Length}, Data: {BitConverter.ToString(pingReply.Buffer, 0, Math.Min(10, pingReply.Buffer.Length))}.");
                    return;
                }

                // create the echoReply
                var icmpPacket = ipPacket.Extract<IcmpV4Packet>();
                icmpPacket.TypeCode = IcmpV4TypeCode.EchoReply;
                icmpPacket.Data = pingReply.Buffer;
                TunnelUtil.UpdateICMPChecksum(icmpPacket);

                ipPacket.DestinationAddress = ipPacket.SourceAddress;
                ipPacket.SourceAddress = pingReply.Address;
                ipPacket.UpdateIPChecksum();
                ipPacket.UpdateCalculatedValues();

                OnPingCompleted?.Invoke(this, new PingCompletedEventArgs(ipPacket));
                if (VhLogger.IsDiagnoseMode)
                    VhLogger.Current.Log(LogLevel.Information, GeneralEventId.Ping, $"Ping Reply has been delegated! DestAddress: {ipPacket?.DestinationAddress}, DataLen: {pingReply.Buffer.Length}, Data: {BitConverter.ToString(pingReply.Buffer, 0, Math.Min(10, pingReply.Buffer.Length))}.");
            }
            catch (Exception ex)
            {
                VhLogger.Current.LogError($"Unexpected exception has been occurred! Message: {ex}.");
            }
        }

        public void Send(IPv4Packet ipPacket)
        {
            // We should not use Task due its stack usage, this method is called by many session each many times!
            var icmpPacket = ipPacket.Extract<IcmpV4Packet>();
            var pingOptions = new PingOptions(ipPacket.TimeToLive - 1, (ipPacket.FragmentFlags & 0x2) != 0);
            _ping.SendAsync(ipPacket.DestinationAddress, _timeout, icmpPacket.Data, pingOptions, ipPacket);

            if (VhLogger.IsDiagnoseMode)
            {
                var buf = icmpPacket.Data ?? new byte[0];
                VhLogger.Current.Log(LogLevel.Information, GeneralEventId.Ping, $"Ping Send has been delegated! DestAddress: {ipPacket?.DestinationAddress}, DataLen: {buf}, Data: {BitConverter.ToString(buf, 0, Math.Min(10, buf.Length))}.");
            }
        }

        public void Dispose()
        {
            _ping.PingCompleted -= Ping_PingCompleted;
            _ping.Dispose();
        }
    }
}

