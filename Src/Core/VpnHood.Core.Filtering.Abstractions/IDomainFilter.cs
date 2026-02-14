
namespace VpnHood.Core.Filtering.Abstractions;

public interface IDomainFilter
{
    FilterAction Process(string? domainName);
}