using System.Collections.Concurrent;
using VpnHood.AccessServer.Abstractions.Providers.Hosts;
using IHostProviderFactory = VpnHood.AccessServer.Providers.Hosts.IHostProviderFactory;

namespace VpnHood.AccessServer.Test.Helper;

public class TestHostProviderFactory : IHostProviderFactory
{
    private readonly ConcurrentDictionary<string, IHostProvider> _hostProviders = new();

    public IHostProvider Create(string providerName, string providerSettings)
    {
        // try get or create the provider from _hostProviders
        return _hostProviders.GetOrAdd(providerName, _ => new TestHostProvider(providerName));
    }
}