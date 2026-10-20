using System.Net;
using System.Net.NetworkInformation;

namespace VpnHood.Test.Providers;

public class TestGatewayIPAddressInformation(IPAddress address) : GatewayIPAddressInformation
{
    public override IPAddress Address => address;
}
