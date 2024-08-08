using VpnHood.AccessServer.Abstractions.Providers.Hosts;
using IHostProviderFactory = VpnHood.AccessServer.Providers.Hosts.IHostProviderFactory;

namespace VpnHood.AccessServer.Test.Helper;

public class TestHostProviderFactory : IHostProviderFactory
{
    public IHostProvider Create(string providerName, string providerSettings)
    {
        if (providerName == TestHostProvider.ProviderName)
            return new TestHostProvider();

        throw new ArgumentException("Unknown Provider in test mode.", nameof(providerName));
    }
}