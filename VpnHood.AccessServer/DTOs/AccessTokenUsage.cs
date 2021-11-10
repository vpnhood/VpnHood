﻿using System;

namespace VpnHood.AccessServer.DTOs
{
    public class AccessTokenUsage
    {
        public long SentTraffic { get; set; }
        public long ReceivedTraffic { get; set; }
        public DateTime? LastTime { get; set; }
        public int ServerCount { get; set; }
        public int DeviceCount { get; set; }
    }
}