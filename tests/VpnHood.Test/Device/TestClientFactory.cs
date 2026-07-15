using VpnHood.Core.Client.VpnServices.Host;
using VpnHood.Core.Filtering.Abstractions;

namespace VpnHood.Test.Device;

// chains the split-db gates over the test's own filters (evaluated first, so their veto wins) and injects
// the test ip mapper that redirects fake test ips to the mock servers
internal sealed class TestClientFactory(NetFilter netFilter) : VpnHoodClientFactory
{
    protected override IIpFilter? CreateInnerIpFilter(VpnHoodClientParams clientParams)
    {
        return netFilter.IpFilter;
    }

    protected override IDomainFilter? CreateInnerDomainFilter(VpnHoodClientParams clientParams)
    {
        return netFilter.DomainFilter;
    }

    protected override IIpMapper? CreateIpMapper(VpnHoodClientParams clientParams)
    {
        return netFilter.IpMapper;
    }
}
