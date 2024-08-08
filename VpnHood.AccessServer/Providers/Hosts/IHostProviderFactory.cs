using VpnHood.AccessServer.Abstractions.Providers.Hosts;

namespace VpnHood.AccessServer.Providers.Hosts;

public interface IHostProviderFactory
{
    IHostProvider Create(string providerName, string providerSettings);
}