using System.Net;

namespace VpnHood.Test
{
    class TestDeviceOptions
    {
        public IPAddress[] TestIpAddresses { get; set; }
        public bool CanSendPacketToOutbound { get; set; } = true;
        public bool IsDnsServerSupported { get; set; } = false;
    }
}
