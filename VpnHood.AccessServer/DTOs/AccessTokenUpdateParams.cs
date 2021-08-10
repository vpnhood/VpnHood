﻿using System;

namespace VpnHood.AccessServer.Controllers.DTOs
{
    public class AccessTokenUpdateParams
    {
        public Wise<string> AccessTokenName { get; set; }
        
        public Wise<Guid> AccessTokenGroupId { get; set; }

        public Wise<DateTime?> EndTime { get; set; } 

        public Wise<int> Lifetime { get; set; } 

        public Wise<int> MaxClient { get; set; } 
        
        public Wise<long> MaxTraffic { get; set; }

        public Wise<string> Url { get; set; }
    }
}
