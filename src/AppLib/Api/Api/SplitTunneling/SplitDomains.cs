namespace VpnHood.AppLib.Api.SplitTunneling;

public class SplitDomains
{
    public required string Includes { get; set; }
    public required string Excludes { get; set; }
    public required string Blocks { get; set; }
}
