namespace VpnHood.Core.DomainFiltering;

public class DomainFilteringPolicy
{
    public string[] Blocks { get; set; } = [];
    public string[] Excludes { get; set; } = [];
    public string[] Includes { get; set; } = [];
}