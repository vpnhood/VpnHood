using System;
using VpnHood.Client.Device;

namespace VpnHood.Client.App
{
    public class UserSettings
    {
        public bool LogToFile { get; set; } = false;
        public bool LogVerbose { get; set; } = true;
        public string CultureName { get; set; } = "en";
        public Guid? DefaultClientProfileId { get; set; }
        public int MaxReconnectCount { get; set; } = 3;
        public int IsDebugMode { get; set; } = 0;
        public string[]? IpGroupFilters { get; set; }
        public FilterMode IpGroupFiltersMode { get; set; } = FilterMode.All;
        public IpRange[]? CustomIpRanges { get; set; }
        public string[]? AppFilters { get; set; }
        public FilterMode AppFiltersMode { get; set; } = FilterMode.All;
        public bool UseUdpChannel { get; set; } = new ClientOptions().UseUdpChannel;
        public bool ExcludeLocalNetwork { get; set; } = new ClientOptions().ExcludeLocalNetwork;
        public IpRange[]? PacketCaptureExcludeIpRanges { get; set; }
        public IpRange[]? PacketCaptureIncludeIpRanges { get; set; }

    }
}
