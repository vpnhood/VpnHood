using VpnHood.Core.Client.Abstractions;

namespace VpnHood.AppLib.Settings;

public class SplitTunnelingSettings
{
    public SplitAppMode AppMode { get; set; } = SplitAppMode.All;
    public string[] Apps { get; set; } = [];
    public SplitCountryMode CountryMode { get; set; } = SplitCountryMode.IncludeAll;
    public string[] Countries { get; set; } = [];
    public bool UseIpViaApp { get; set; }
    public bool UseIpViaDevice { get; set; }
    public bool UseDomain { get; set; }
    public bool UseLocalNetwork { get; set; }
    public SplitDnsMode DnsMode { get; set; } = SplitDnsMode.IncludeAll;
    public SplitUnsupportedIpMode UnsupportedIpMode { get; set; } = SplitUnsupportedIpMode.Exclude;
}
