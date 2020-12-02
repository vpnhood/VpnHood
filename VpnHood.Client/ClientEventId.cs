namespace VpnHood.Client
{
    public static class ClientEventId
    {
        public enum Event
        {
            Start = 500,
            DnsRequest,
            DnsReply,
        }

        public static Microsoft.Extensions.Logging.EventId DnsRequest = new Microsoft.Extensions.Logging.EventId((int)Event.DnsRequest, DnsRequest.Name);
        public static Microsoft.Extensions.Logging.EventId DnsReply = new Microsoft.Extensions.Logging.EventId((int)Event.DnsReply, DnsReply.Name);
    }
}
