using VpnHood.AccessServer.Abstractions.Providers.Hosts;

namespace VpnHood.AccessServer.Providers.Hosts;

public interface IHostProviderFactory
{
    public IHostProvider Create(Guid hostProviderId, string hostProviderName, string providerSettings);
}