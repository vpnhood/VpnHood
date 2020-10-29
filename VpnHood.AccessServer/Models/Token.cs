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
        public const string expirationTime_ = nameof(expirationTime);
        public const string maxClientCount_ = nameof(maxClientCount);
        public const string isPublic_ = nameof(isPublic);
        
        public Guid tokenId { get; set; }
        public string tokenName { get; set; }
        public int supportId { get; set; }
        public byte[] secret { get; set; }
        public string dnsName { get; set; }
        public string serverEndPoint { get; set; }
        public long maxTraffic { get; set; }
        public DateTime? expirationTime { get; set; }
        public int maxClientCount { get; set; }
        public bool isPublic { get; set; }
    }
}
