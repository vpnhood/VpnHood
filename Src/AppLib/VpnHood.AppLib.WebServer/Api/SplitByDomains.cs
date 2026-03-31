namespace VpnHood.AppLib.WebServer.Api;

public class SplitByDomains
{
    public required string Includes { get; set; }
    public required string Excludes { get; set; }
    public required string Blocks { get; set; }
}
