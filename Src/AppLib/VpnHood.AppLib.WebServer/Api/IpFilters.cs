
namespace VpnHood.AppLib.WebServer.Api;

public class IpFilters
{
    public required string VpnAdapterIpFilterInclude { get; set; }
    public required string VpnAdapterIpFilterExclude { get; set; }
    public required string AppIpFilterInclude { get; set; }
    public required string AppIpFilterExclude { get; set; }
}