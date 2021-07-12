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

        private readonly TestDeviceOptions _options;

        public string OperatingSystemInfo => Environment.OSVersion.ToString() + ", " + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit");

        public bool IsExcludeAppsSupported => false;

        public bool IsIncludeAppsSupported => false;

        public DeviceAppInfo[] InstalledApps => throw new NotSupportedException();

        public TestDevice(TestDeviceOptions options)
        {
            _options = options;
        }

        public Task<IPacketCapture> CreatePacketCapture()
        {
            var res = new TestPacketCapture(_options);
            return Task.FromResult((IPacketCapture)res);
        }
    }
}
