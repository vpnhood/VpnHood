using System.Net;

namespace VpnHood.Test
{
    internal class TestDeviceOptions
    {
        public IPAddress[] TestIpAddresses { get; set; } = TestHelper.GetTestIpAddresses();
        public bool CanSendPacketToOutbound { get; set; } = true;
        public bool IsDnsServerSupported { get; set; } = false;
    }
}