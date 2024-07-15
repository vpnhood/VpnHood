using VpnHood.Client.App.Abstractions;
using VpnHood.Client.Device;

namespace VpnHood.Test.Device;

internal class TestDevice(TestDeviceOptions options, bool useNullPacketCapture) : IDevice
{
#pragma warning disable CS0067 // The event 'TestDevice.StartedAsService' is never used
    public event EventHandler? StartedAsService;
#pragma warning restore CS0067 // The event 'TestDevice.StartedAsService' is never used
    public IAppCultureService? CultureService => null;
    public string OsInfo => Environment.OSVersion + ", " + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit");
    public bool IsExcludeAppsSupported => false;
    public bool IsIncludeAppsSupported => false;
    public bool IsAlwaysOnSupported => false;

    public DeviceAppInfo[] InstalledApps => throw new NotSupportedException();

    public Task<IPacketCapture> CreatePacketCapture(IUiContext? uiContext)
    {
        IPacketCapture res = useNullPacketCapture
            ? new NullPacketCapture()
            : new TestPacketCapture(options);

        return Task.FromResult(res);
    }
    public void Dispose()
    {
    }
}