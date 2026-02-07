namespace VpnHood.Core.SniFiltering;

public class DomainFilterPolicy
{
    public string[] Blocks { get; set; } = [];
    public string[] Excludes { get; set; } = [];
    public string[] Includes { get; set; } = [];
}