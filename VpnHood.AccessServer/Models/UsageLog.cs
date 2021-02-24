using System;

namespace VpnHood.AccessServer.Models
{
    public class UsageLog
    {
        public const string Table_ = nameof(UsageLog);
        public const string usageLogId_ = nameof(usageLogId);
        public const string accessTokenId_ = nameof(accessTokenId);
        public const string clientId_ = nameof(clientId);
        public const string clientIp_ = nameof(clientIp);
        public const string clientVersion_ = nameof(clientVersion);
        public const string sentTraffic_ = nameof(sentTraffic);
        public const string receivedTraffic_ = nameof(receivedTraffic);
        public const string cycleSentTraffic_ = nameof(cycleSentTraffic);
        public const string cycleReceivedTraffic_ = nameof(cycleReceivedTraffic);
        public const string totalSentTraffic_ = nameof(totalSentTraffic);
        public const string totalReceivedTraffic_ = nameof(totalReceivedTraffic);
        public const string createdTime_ = nameof(createdTime);

        public long usageLogId { get; set; }
        public Guid accessTokenId { get; set; }
        public Guid clientId { get; set; }
        public string clientIp { get; set; }
        public string clientVersion { get; set; }
        public long sentTraffic { get; set; }
        public long receivedTraffic { get; set; }
        public long cycleSentTraffic { get; set; }
        public long cycleReceivedTraffic { get; set; }
        public long totalSentTraffic { get; set; }
        public long totalReceivedTraffic { get; set; }
        public DateTime createdTime { get; set; }
    }
}
