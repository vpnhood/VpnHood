using System.Text.Json.Serialization;
using VpnHood.Common.Net;

namespace VpnHood.Server.Configurations;

public class NetFilterOptions
{
    public bool? ExcludeLocalNetwork { get; set; }
    public IpRange[]? PacketCaptureIncludeIpRanges { get; set; }
    public IpRange[]? PacketCaptureExcludeIpRanges { get; set; }
    public IpRange[]? IncludeIpRanges { get; set; }
    public IpRange[]? ExcludeIpRanges { get; set; }

    [JsonIgnore] public bool ExcludeLocalNetworkValue => ExcludeLocalNetwork ?? true;

}