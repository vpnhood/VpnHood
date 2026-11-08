using VpnHood.Core.Client;
using VpnHood.Core.Client.VpnServices.Host;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Test.Device;

// chains the split-db gates over the test's own filters (evaluated first, so their veto wins) and injects
// the test ip mapper that redirects fake test ips to the mock servers; hands each client it makes to the test
internal sealed class TestClientFactory(NetFilter testNetFilter, Action<VpnHoodClient> clientCreated)
    : VpnHoodClientFactory
{
    public override async Task<VpnHoodClient> Create(VpnHoodClientParams clientParams, NetFilter netFilter,
        CancellationToken cancellationToken)
    {
        var client = await base.Create(clientParams, netFilter, cancellationToken).Vhc();
        clientCreated(client);
        return client;
    }

    protected override IIpFilter? CreateInnerIpFilter(VpnHoodClientParams clientParams)
    {
        return testNetFilter.IpFilter;
    }

    protected override IDomainFilter? CreateInnerDomainFilter(VpnHoodClientParams clientParams)
    {
        return testNetFilter.DomainFilter;
    }

    public override IIpMapper? CreateIpMapper(VpnHoodClientParams clientParams)
    {
        return testNetFilter.IpMapper;
    }
}
