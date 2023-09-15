using System;
using VpnHood.Client.App;
using VpnHood.Client.Device;

namespace VpnHood.Test;

internal class TestAppProvider : IAppProvider
{
    public TestAppProvider(TestDeviceOptions? testDeviceOptions, Uri? updateInfoUrl = null)
    {
        UpdateInfoUrl = updateInfoUrl;
        Device = TestHelper.CreateDevice(testDeviceOptions);
        UpdateInfoUrl = updateInfoUrl;
    }

    public IDevice Device { get; }
    public bool IsLogToConsoleSupported => true;
    public Uri? AdditionalUiUrl => null;
    public Uri? UpdateInfoUrl { get; }
}