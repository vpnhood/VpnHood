using System;

namespace VpnHood.Server
{
    public class TokenSettings
    {
        public long MaxTransferByteCount { get; set; }
        public DateTime? ExpirationTime { get; set; }
        public int MaxClientCount { get; set; } = 2;
    }
}