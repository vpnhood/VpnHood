namespace VpnHood.AppLib.WebServer.Api;

public class SplitDomains
{
    public required string Includes { get; set; }
    public required string Excludes { get; set; }
    public required string Blocks { get; set; }
}
