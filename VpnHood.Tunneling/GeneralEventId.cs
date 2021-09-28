using Microsoft.Extensions.Logging;

namespace VpnHood.Tunneling
{
    public static class GeneralEventId
    {
        public static EventId Hello = new((int) EventCode.Hello, nameof(Hello));
        public static EventId Nat = new((int) EventCode.Nat, nameof(Nat));
        public static EventId Ping = new((int) EventCode.Ping, nameof(Ping));
        public static EventId Dns = new((int) EventCode.Dns, nameof(Dns));
        public static EventId Tcp = new((int) EventCode.Tcp, nameof(Tcp));
        public static EventId Udp = new((int) EventCode.Udp, nameof(Udp));
        public static EventId StreamChannel = new((int) EventCode.StreamChannel, EventCode.StreamChannel.ToString());

        public static EventId DatagramChannel =
            new((int) EventCode.DatagramChannel, EventCode.DatagramChannel.ToString());

        private enum EventCode
        {
            Hello = 10,
            Nat,
            Ping,
            Dns,
            Tcp,
            Udp,
            StreamChannel,
            DatagramChannel
        }
    }
}