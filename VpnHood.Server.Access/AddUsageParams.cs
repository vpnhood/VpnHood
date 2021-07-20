namespace VpnHood.Server
{
    public class AddUsageParams
    {
        public string ServerId { get; set; }
        public ClientIdentity ClientIdentity { get; set; }
        public long SentTrafficByteCount { get; set; }
        public long ReceivedTrafficByteCount { get; set; }
    }
}
