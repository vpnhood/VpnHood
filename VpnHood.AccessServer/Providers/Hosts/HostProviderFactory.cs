using System.Collections.Concurrent;
using VpnHood.AccessServer.Abstractions.Providers.Hosts;
using VpnHood.Common.Utils;

namespace VpnHood.AccessServer.Providers.Hosts;

public class HostProviderFactory : IHostProviderFactory
{
    private readonly ConcurrentDictionary<string, IHostProvider> _hostProviders = new();

    public IHostProvider Create(string providerName, string providerSettings)
    {
        if (providerName.Contains("fake.internal")) {
            var settings = VhUtil.JsonDeserialize<FakeHostProvider.Settings>(providerSettings);
            return _hostProviders.GetOrAdd(providerName, _ => new FakeHostProvider(providerName, settings));
        }

        throw new Exception($"Unknown provider: {providerName}");
    }

}