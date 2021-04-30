using VpnHood.Client.App;
using VpnHood.Client.Device;

namespace VpnHood.Test
{
    class TestAppProvider : IAppProvider
    {
        public IDevice Device { get; } = TestHelper.CreateDevice();
    }
}
