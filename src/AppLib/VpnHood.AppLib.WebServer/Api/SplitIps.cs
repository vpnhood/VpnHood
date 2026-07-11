namespace VpnHood.AppLib.WebServer.Api;

public class SplitIps
{
    public required string DeviceIncludes { get; set; }
    public required string DeviceExcludes { get; set; }
    public required string AppIncludes { get; set; }
    public required string AppExcludes { get; set; }
    public required string AppBlocks { get; set; }
}