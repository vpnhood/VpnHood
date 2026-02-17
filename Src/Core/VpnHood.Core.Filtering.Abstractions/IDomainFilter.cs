
namespace VpnHood.Core.Filtering.Abstractions;

public interface IDomainFilter : IDisposable
{
    FilterAction Process(string? domainName);
}