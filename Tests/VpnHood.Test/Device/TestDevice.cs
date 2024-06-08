using VpnHood.Client.App.Abstractions;
using VpnHood.Client.Device;

namespace VpnHood.Test.Device;

internal class TestDevice(TestDeviceOptions? options = default) : IDevice
{
    private readonly TestDeviceOptions _options = options ?? new TestDeviceOptions();

#pragma warning disable CS0067 // The event 'TestDevice.StartedAsService' is never used
    public event EventHandler? StartedAsService;
#pragma warning restore CS0067 // The event 'TestDevice.StartedAsService' is never used
    public IAppCultureService? CultureService => null;
    public string OsInfo => Environment.OSVersion + ", " + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit");
    public bool IsExcludeAppsSupported => false;
    public bool IsIncludeAppsSupported => false;
    public bool IsLogToConsoleSupported => true;
    public bool IsAlwaysOnSupported => false;

    public DeviceAppInfo[] InstalledApps => throw new NotSupportedException();

    public Task<IPacketCapture> CreatePacketCapture(IUiContext? uiContext)
    {
        var res = new TestPacketCapture(_options);
        return Task.FromResult((IPacketCapture)res);
    }
    public void Dispose()
    {
    }
}