namespace VpnHood.AccessServer.Models
{
    public class AccessUsage
    {
        public const string Table_ = nameof(AccessUsage); 
        public const string accessTokenId_ = nameof(accessTokenId); 
        public const string clientIp_ = nameof(clientIp);
        public const string cycleSentTraffic_ = nameof(cycleSentTraffic);
        public const string cycleReceivedTraffic_ = nameof(cycleReceivedTraffic);
        public const string totalSentTraffic_ = nameof(totalSentTraffic);
        public const string totalReceivedTraffic_ = nameof(totalReceivedTraffic);

        public long accessTokenId { get; set; }
        public long clientIp { get; set; }
        public long cycleSentTraffic { get; set; }
        public long cycleReceivedTraffic { get; set; }
        public long totalSentTraffic { get; set; }
        public long totalReceivedTraffic { get; set; }
    }
}
