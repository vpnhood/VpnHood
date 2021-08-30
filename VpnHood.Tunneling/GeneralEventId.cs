using Microsoft.Extensions.Logging;

namespace VpnHood.Tunneling
{
    public static class GeneralEventId
    {
        public static EventId Hello = new((int) EventCode.Hello, EventCode.Hello.ToString());
        public static EventId Nat = new((int) EventCode.Nat, EventCode.Nat.ToString());
        public static EventId Ping = new((int) EventCode.Ping, EventCode.Ping.ToString());
        public static EventId Dns = new((int) EventCode.Dns, EventCode.Dns.ToString());
        public static EventId Tcp = new((int) EventCode.Tcp, EventCode.Tcp.ToString());
        public static EventId Udp = new((int) EventCode.Udp, EventCode.Udp.ToString());
        public static EventId StreamChannel = new((int) EventCode.StreamChannel, EventCode.StreamChannel.ToString());

        public static EventId DatagramChannel =
            new((int) EventCode.DatagramChannel, EventCode.DatagramChannel.ToString());

        private enum EventCode
        {
            Start = 10,
            Hello,
            Nat,
            Ping,
            Dns,
            Tcp,
            Udp,
            StreamChannel,
            DatagramChannel,
            UdpChannel
        }
    }
}