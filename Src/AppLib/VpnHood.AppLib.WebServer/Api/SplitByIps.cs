namespace VpnHood.AppLib.WebServer.Api;

public class SplitByIps
{
    public required string DeviceIncludes { get; set; }
    public required string DeviceExcludes { get; set; }
    public required string AppIncludes { get; set; }
    public required string AppExcludes { get; set; }
}