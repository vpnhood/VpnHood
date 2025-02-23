
namespace VpnHood.AppLib.WebServer.Api;

public class IpFilters
{
    public required string AdapterIpFilterIncludes { get; set; }
    public required string AdapterIpFilterExcludes { get; set; }
    public required string AppIpFilterIncludes { get; set; }
    public required string AppIpFilterExcludes { get; set; }
}