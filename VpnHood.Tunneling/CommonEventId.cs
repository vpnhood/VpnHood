using Microsoft.Extensions.Logging;

namespace VpnHood.Tunneling
{
    public static class CommonEventId
    {
        private enum Event
        {
            Start = 10,
            Nat,
        }

        public static EventId Nat = new EventId((int)Event.Nat, Nat.Name);
    }
}
