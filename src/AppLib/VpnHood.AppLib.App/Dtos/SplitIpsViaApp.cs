namespace VpnHood.AppLib.Dtos;

public class SplitIpsViaApp
{
    public required string Includes { get; set; }
    public required string Excludes { get; set; }
    public required string Blocks { get; set; }
}
