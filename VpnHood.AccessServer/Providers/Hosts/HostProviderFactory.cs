using GrayMint.Common.Utils;
using VpnHood.AccessServer.Abstractions.Providers.Hosts;
using VpnHood.AccessServer.HostProviders.Ovh;
using VpnHood.AccessServer.HostProviders.Ovh.Dto;

namespace VpnHood.AccessServer.Providers.Hosts;

public class HostProviderFactory(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<HostProviderFactory> logger
    ) : IHostProviderFactory
{
    public IHostProvider Create(Guid hostProviderId, string hostProviderName, string providerSettings)
    {
        if (hostProviderName.Contains("fake.internal", StringComparison.OrdinalIgnoreCase)) {
            return FakeHostProvider.Create(serviceScopeFactory, hostProviderId).Result;
        }

        if (hostProviderName.Contains("ovhcloud.com", StringComparison.OrdinalIgnoreCase) ||
            hostProviderName.Contains("ovh.com", StringComparison.OrdinalIgnoreCase)) {
            return new OvhHostProvider(logger, GmUtil.JsonDeserialize<OvhHostProviderSettings>(providerSettings));
        }


        throw new Exception($"Unknown provider: {hostProviderName}");
    }
}