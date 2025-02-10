using System.Text.Json.Serialization;

namespace VpnHood.AppLib.WebServer.Api;

public class IpFilters
{
    [JsonPropertyName("packetCaptureIpFilterInclude")] //todo
    public required string VpnAdapterIpFilterInclude { get; set; }
    [JsonPropertyName("packetCaptureIpFilterExclude")] //todo
    public required string VpnAdapterIpFilterExclude { get; set; }
    public required string AppIpFilterInclude { get; set; }
    public required string AppIpFilterExclude { get; set; }
}