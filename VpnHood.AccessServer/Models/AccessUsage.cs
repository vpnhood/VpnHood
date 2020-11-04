namespace VpnHood.AccessServer.Models
{
    public class AccessUsage
    {
        public const string ClientUsage_ = nameof(AccessUsage); 
        public const string tokenId_ = nameof(tokenId); 
        public const string clientIp_ = nameof(clientIp);
        public const string sentTraffic_ = nameof(sentTraffic);
        public const string receivedTraffic_ = nameof(receivedTraffic);
        public const string totalSentTraffic_ = nameof(totalSentTraffic);
        public const string totalReceivedTraffic_ = nameof(totalReceivedTraffic);

        public long tokenId { get; set; }
        public long clientIp { get; set; }
        public long sentTraffic { get; set; }
        public long receivedTraffic { get; set; }
        public long totalSentTraffic { get; set; }
        public long totalReceivedTraffic { get; set; }
    }
}
