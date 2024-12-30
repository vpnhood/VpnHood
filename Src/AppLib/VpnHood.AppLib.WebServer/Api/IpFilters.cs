namespace VpnHood.AppLib.WebServer.Api;

public class IpFilters
{
    public required string PacketCaptureIpFilterInclude { get; set; }
    public required string PacketCaptureIpFilterExclude { get; set; }
    public required string AppIpFilterInclude { get; set; }
    public required string AppIpFilterExclude { get; set; }
}