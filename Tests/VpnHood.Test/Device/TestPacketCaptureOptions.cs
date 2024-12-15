using System.Net;

namespace VpnHood.Test.Device;

public class TestPacketCaptureOptions
{
    public bool CanSendPacketToOutbound { get; set; } = true;
    public bool IsDnsServerSupported { get; set; }
    
    // DNS requests are always captured by DNS. Test Adapter ignores them if CaptureDnsAddresses is specified
    public IPAddress[]? CaptureDnsAddresses { get; set; }
}