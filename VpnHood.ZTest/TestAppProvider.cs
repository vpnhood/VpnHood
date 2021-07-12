using VpnHood.Client.App;
using VpnHood.Client.Device;

namespace VpnHood.Test
{
    class TestAppProvider : IAppProvider
    {
        public TestAppProvider(TestDeviceOptions testDeviceOptions)
        {
            Device = TestHelper.CreateDevice(testDeviceOptions);
        }

        public IDevice Device { get; }
    }
}
