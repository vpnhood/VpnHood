namespace VpnHood.AppLib.WebServer.Api;

public class IpFilters
{
    public required string DeviceIpFilterInclude { get; set; }
    public required string DeviceIpFilterExclude { get; set; }
    public required string AppIpFilterInclude { get; set; }
    public required string AppIpFilterExclude { get; set; }
}