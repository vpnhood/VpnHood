using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.VpnAdapters.Abstractions;

namespace VpnHood.Test.Providers;

// an adapter whose NAT translates no IP version, which a server cannot work with
public class TestNoNatVpnAdapter() : NullVpnAdapter(blocking: false)
{
    public override bool IsNatSupported(IpVersion ipVersion) => false;
}
