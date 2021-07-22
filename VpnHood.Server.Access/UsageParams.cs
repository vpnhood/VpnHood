namespace VpnHood.Server
{
    public class UsageParams
    {
        public ClientIdentity ClientIdentity { get; set; }
        public long SentTrafficByteCount { get; set; }
        public long ReceivedTrafficByteCount { get; set; }
    }
}
