namespace VpnHood.Core.DomainFiltering;

public class DomainFilterPolicy
{
    public string[] Blocks { get; set; } = [];
    public string[] Excludes { get; set; } = [];
    public string[] Includes { get; set; } = [];
}