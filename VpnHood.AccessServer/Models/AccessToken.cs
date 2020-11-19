using System;

namespace VpnHood.AccessServer.Models
{

    public class AccessToken
    {
        public const string Table_ = nameof(AccessToken);
        public const string accessTokenId_ = nameof(accessTokenId);
        public const string accessTokenName_ = nameof(accessTokenName);
        public const string supportId_ = nameof(supportId);
        public const string secret_ = nameof(secret);
        public const string serverEndPoint_ = nameof(serverEndPoint);
        public const string maxTraffic_ = nameof(maxTraffic);
        public const string startTime_ = nameof(startTime);
        public const string endTime_ = nameof(endTime);
        public const string lifeTime_ = nameof(lifetime);
        public const string maxClient_ = nameof(maxClient);
        public const string isPublic_ = nameof(isPublic);
        
        public Guid accessTokenId { get; set; }
        public string accessTokenName { get; set; }
        public int supportId { get; set; }
        public byte[] secret { get; set; }
        public string serverEndPoint { get; set; }
        public string serverPort { get; set; }
        public long maxTraffic { get; set; }
        public int lifetime { get; set; }
        public DateTime? startTime { get; set; }
        public DateTime? endTime { get; set; }
        public int maxClient { get; set; }
        public bool isPublic { get; set; }
    }
}
