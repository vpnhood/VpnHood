namespace VpnHood.Server
{
    public class UsageParams
    {
        public string AccessId { get; set; }
        public ClientIdentity ClientIdentity { get; set; }
        public long SentTrafficByteCount { get; set; }
        public long ReceivedTrafficByteCount { get; set; }
    }
}
