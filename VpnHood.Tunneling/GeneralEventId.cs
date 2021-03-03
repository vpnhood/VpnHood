using Microsoft.Extensions.Logging;

namespace VpnHood.Tunneling
{
    public static class GeneralEventId
    {
        private enum EventCode
        {
            Start = 10,
            Hello,
            Nat,
            Ping,
            Dns,
            Tcp,
            TcpProxy,
            TcpDatagram,
            Udp,
        }

        public static EventId Hello = new EventId((int)EventCode.Hello, EventCode.Hello.ToString());
        public static EventId Nat = new EventId((int)EventCode.Nat, EventCode.Nat.ToString());
        public static EventId Ping = new EventId((int)EventCode.Ping, EventCode.Ping.ToString());
        public static EventId Dns = new EventId((int)EventCode.Dns, EventCode.Dns.ToString());
        public static EventId Tcp = new EventId((int)EventCode.Tcp, EventCode.Tcp.ToString());
        public static EventId Udp = new EventId((int)EventCode.Udp, EventCode.Udp.ToString());
        public static EventId TcpDatagram = new EventId((int)EventCode.TcpDatagram, EventCode.TcpDatagram.ToString());
        public static EventId TcpProxy = new EventId((int)EventCode.TcpProxy, EventCode.TcpProxy.ToString());
    }
}
