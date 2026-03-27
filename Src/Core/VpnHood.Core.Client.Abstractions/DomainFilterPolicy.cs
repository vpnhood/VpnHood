namespace VpnHood.Core.Client.Abstractions;

public class DomainFilterPolicy
{
    public IReadOnlyList<string> Blocks { get; set; } = [];
    public IReadOnlyList<string> Excludes { get; set; } = [];
    public IReadOnlyList<string> Includes { get; set; } = [];
}