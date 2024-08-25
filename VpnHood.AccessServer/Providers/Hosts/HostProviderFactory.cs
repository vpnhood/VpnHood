using VpnHood.AccessServer.Abstractions.Providers.Hosts;

namespace VpnHood.AccessServer.Providers.Hosts;

public class HostProviderFactory(
    IServiceScopeFactory serviceScopeFactory
    ) : IHostProviderFactory
{
    public IHostProvider Create(Guid hostProviderId, string hostProviderName, string providerSettings)
    {
        if (hostProviderName.Contains("fake.internal", StringComparison.OrdinalIgnoreCase)) {
            return FakeHostProvider.Create(serviceScopeFactory, hostProviderId).Result;
        }

        throw new Exception($"Unknown provider: {hostProviderName}");
    }
}