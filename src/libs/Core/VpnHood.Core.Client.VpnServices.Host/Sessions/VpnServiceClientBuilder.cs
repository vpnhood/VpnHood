using Microsoft.Extensions.Logging;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Client.VpnServices.Host.Sessions;

// Builds a connect's client from the platform's factory. Each part is its maker's to close until the next one owns it,
// as the filters hold their split databases open: a failure closes what was made so far, and is the one thrown
internal static class VpnServiceClientBuilder
{
    // the client and its filters, which it owns once made
    public static async Task<VpnHoodClient> Build(VpnHoodClientFactory clientFactory, VpnHoodClientParams clientParams,
        CancellationToken cancellationToken)
    {
        var netFilter = await BuildNetFilter(clientFactory, clientParams, cancellationToken).Vhc();
        try {
            return await clientFactory.Create(clientParams, netFilter, cancellationToken).Vhc();
        }
        catch {
            netFilter.Dispose();
            throw;
        }
    }

    // the filters made so far are closed where a later part fails, as a derived factory's mapper may, each whatever
    // another's close does
    public static async Task<NetFilter> BuildNetFilter(VpnHoodClientFactory clientFactory,
        VpnHoodClientParams clientParams, CancellationToken cancellationToken)
    {
        var ipFilter = await clientFactory.CreateIpFilter(clientParams, cancellationToken).Vhc();
        try {
            var domainFilter = await clientFactory.CreateDomainFilter(clientParams, cancellationToken).Vhc();
            try {
                return new NetFilter {
                    IpFilter = ipFilter,
                    DomainFilter = domainFilter,
                    IpMapper = clientFactory.CreateIpMapper(clientParams)
                };
            }
            catch {
                VhInvoker.TryInvoke("close a domain filter", LogLevel.Error, domainFilter.Dispose);
                throw;
            }
        }
        catch {
            VhInvoker.TryInvoke("close an IP filter", LogLevel.Error, ipFilter.Dispose);
            throw;
        }
    }
}
