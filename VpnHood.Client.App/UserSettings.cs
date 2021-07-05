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
        public string[] IncludeNetworks { get; set; } = Array.Empty<string>();
        public string[] ExcludeNetworks { get; set; } = Array.Empty<string>();
        public string[] IncludeIpNetworkGroups { get; set; } = Array.Empty<string>();
        public string[] IpGroupFilters { get; set; }
        public FilterMode IpGroupFiltersMode { get; set; } = FilterMode.All;
        public IPNetwork[] CustomIpNetwork { get; set; }
        public string[] AppFilters { get; set; } = Array.Empty<string>();
        public FilterMode AppFiltersMode { get; set; } = FilterMode.All;
        public bool UseUdpChannel { get; set; } = false;
    }
}
