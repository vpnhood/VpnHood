using System.Collections.Concurrent;
using VpnHood.AccessServer.Abstractions.Providers.Hosts;
using VpnHood.Common.Utils;

namespace VpnHood.AccessServer.Providers.Hosts;

public class HostProviderFactory(IServiceProvider serviceProvider) : IHostProviderFactory
{
    private readonly ConcurrentDictionary<string, IHostProvider> _fakeProviders = new();

    public IHostProvider Create(string providerName, string providerSettings)
    {
        if (providerName.Contains("fake.internal")) {
            var settings = VhUtil.JsonDeserialize<FakeHostProvider.Settings>(providerSettings);
            var logger = serviceProvider.GetRequiredService<ILogger<FakeHostProvider>>();
            return _fakeProviders.GetOrAdd(providerName,
                _ => {
                    logger.LogInformation("Creating FakeHostProvider. ProviderName: {ProviderName}", providerName);
                    return new FakeHostProvider(providerName, settings, logger);
                });
        }

        throw new Exception($"Unknown provider: {providerName}");
    }

}