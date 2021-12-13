﻿using System;
using System.ComponentModel;
using System.Text.Json.Serialization;
using VpnHood.Common.Trackers;
using VpnHood.Server.SystemInformation;
using VpnHood.Tunneling;
using VpnHood.Tunneling.Factory;

namespace VpnHood.Server
{
    public class ServerOptions
    {
        public SocketFactory SocketFactory { get; set; } = new();
        public ITracker? Tracker { get; set; }
        public ISystemInfoProvider? SystemInfoProvider { get; set; }
        public bool AutoDisposeAccessServer { get; set; } = true;
        public TimeSpan ConfigureInterval { get; set; } = TimeSpan.FromSeconds(60);
    }
}