using VpnHood.Client.App.Abstractions;
using VpnHood.Client.Device;

namespace VpnHood.Test;

internal class TestAppService(TestDeviceOptions? testDeviceOptions) : IAppService
{
    public IDevice Device { get; } = TestHelper.CreateDevice(testDeviceOptions);
    public bool IsLogToConsoleSupported => true;
}