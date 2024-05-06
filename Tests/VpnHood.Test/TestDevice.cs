using VpnHood.Client.App.Abstractions;
using VpnHood.Client.Device;

namespace VpnHood.Test;

internal class TestDevice(TestDeviceOptions? options = default) : IDevice
{
    private readonly TestDeviceOptions _options = options ?? new TestDeviceOptions();

    public event EventHandler? StartedAsService;
    public IAppCultureService? CultureService => null;
    public string OsInfo => Environment.OSVersion + ", " + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit");
    public bool IsExcludeAppsSupported => false;
    public bool IsIncludeAppsSupported => false;
    public bool IsLogToConsoleSupported => true;
    public bool IsAlwaysOnSupported => false;

    public DeviceAppInfo[] InstalledApps => throw new NotSupportedException();

    public Task<IPacketCapture> CreatePacketCapture()
    {
        var res = new TestPacketCapture(_options);
        return Task.FromResult((IPacketCapture)res);
    }
    public void Dispose()
    {
    }
}