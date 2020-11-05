using System;

namespace VpnHood.AccessServer.Models
{

    public class Token
    {
        public const string Token_ = nameof(Token);
        public const string tokenId_ = nameof(tokenId);
        public const string tokenName_ = nameof(tokenName);
        public const string supportId_ = nameof(supportId);
        public const string secret_ = nameof(secret);
        public const string dnsName_ = nameof(dnsName);
        public const string serverEndPoint_ = nameof(serverEndPoint);
        public const string maxTraffic_ = nameof(maxTraffic);
        public const string startTime_ = nameof(startTime);
        public const string endTime_ = nameof(endTime);
        public const string lifeTime_ = nameof(lifetime);
        public const string maxClient_ = nameof(maxClient);
        public const string isPublic_ = nameof(isPublic);
        
        public Guid tokenId { get; set; }
        public string tokenName { get; set; }
        public int supportId { get; set; }
        public byte[] secret { get; set; }
        public string dnsName { get; set; }
        public string serverEndPoint { get; set; }
        public long maxTraffic { get; set; }
        public int lifetime { get; set; }
        public DateTime? startTime { get; set; }
        public DateTime? endTime { get; set; }
        public int maxClient { get; set; }
        public bool isPublic { get; set; }
    }
}
