using System.Net;

namespace VpnHood.Test;

internal class TestDeviceOptions
{
    public bool CanSendPacketToOutbound { get; set; } = true;
    public bool IsDnsServerSupported { get; set; } = false;

    // DNS requests are always captured by DNS. Test Adapter ignores them if CaptureDnsAddresses is specified
    public IPAddress[]? CaptureDnsAddresses { get; set; }
}