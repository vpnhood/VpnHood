using System.Net;

namespace VpnHood.Test.Device;

public class TestVpnAdapterOptions
{
    public bool IsDnsServerSupported { get; set; }

    // DNS requests are always captured by DNS. Test Adapter ignores them if CaptureDnsAddresses is specified
    public IPAddress[]? CaptureDnsAddresses { get; set; }
}