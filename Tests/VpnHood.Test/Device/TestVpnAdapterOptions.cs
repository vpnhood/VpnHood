using System.Net;

namespace VpnHood.Test.Device;

public class TestVpnAdapterOptions
{

    // DNS requests are always captured by DNS. Test Adapter ignores them if CaptureDnsAddresses is specified
    public IPAddress[]? CaptureDnsAddresses { get; set; }
}