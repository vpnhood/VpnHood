using System;

namespace VpnHood.AccessServer.DTOs
{
    public class AccessTokenCreateParams
    {
        public Guid? AccessTokenId { get; set; }
        public Guid AccessPointGroupId { get; set; }
        public string? AccessTokenName { get; set; }
        public byte[]? Secret { get; set; }
        public long MaxTraffic { get; set; }
        public int Lifetime { get; set; }
        public int MaxClient { get; set; }
        public DateTime? EndTime { get; set; }
        public string? Url { get; set; }
        public bool IsPublic { get; set; }
    }
}