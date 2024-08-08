using VpnHood.AccessServer.Abstractions.Providers.Hosts;

namespace VpnHood.AccessServer.Providers.Hosts;

public class HostProviderFactory : IHostProviderFactory
{
    public IHostProvider Create(string providerName, string providerSettings)
    {
        throw new NotImplementedException();
    }

    public Task<IHostProvider> CreateAll()
    {
        throw new NotImplementedException();
    }
}