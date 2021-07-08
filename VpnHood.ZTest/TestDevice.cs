using VpnHood.Client;
using System.Net;
using System.Threading.Tasks;
using System;
using VpnHood.Client.Device;

namespace VpnHood.Test
{
    class TestDevice : IDevice
    {
#pragma warning disable 0067
        public event EventHandler OnStartAsService;
#pragma warning restore 0067

        private readonly IPAddress[] _testIpAddresses;

        public string OperatingSystemInfo => Environment.OSVersion.ToString() + ", " + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit");

        public bool IsExcludeAppsSupported => false;

        public bool IsIncludeAppsSupported => false;

        public bool IsExcludeNetworksSupported => true;

        public bool IsIncludeNetworksSupported => true;

        public DeviceAppInfo[] InstalledApps => throw new NotSupportedException();

        public TestDevice(IPAddress[] testIpAddresses)
        {
            _testIpAddresses = testIpAddresses;
        }

        public Task<IPacketCapture> CreatePacketCapture()
        {
            var res = new TestPacketCapture(_testIpAddresses);
            return Task.FromResult((IPacketCapture)res);
        }
    }
}
