using Microsoft.Extensions.Logging;
using System;

namespace VpnHood
{
    public static class CommonEventId
    {
        private enum Event
        {
            Start = 10,
            Nat,
        }

        public static Microsoft.Extensions.Logging.EventId Nat = new Microsoft.Extensions.Logging.EventId((int)Event.Nat, Nat.Name);
    }
}
