using Microsoft.Extensions.Logging;

namespace VpnHood.Tunneling
{
    public static class CommonEventId
    {
        private enum Event
        {
            Start = 10,
            Nat,
            Ping,
        }

        public static EventId Nat = new EventId((int)Event.Nat, Event.Nat.ToString());
        public static EventId Ping = new EventId((int)Event.Ping, Event.Ping.ToString());
    }
}
