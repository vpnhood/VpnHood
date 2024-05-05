using VpnHood.Client.App.Abstractions;
using VpnHood.Client.Device;

namespace VpnHood.Test;

internal class TestDevice(TestDeviceOptions? options = default) : IDevice
{
    private readonly TestDeviceOptions _options = options ?? new TestDeviceOptions();

#pragma warning disable 0067
    public event EventHandler? StartedAsService;
#pragma warning restore 0067
    public IAppCultureService? CultureService => null;
    public string OsInfo => Environment.OSVersion + ", " + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit");
    public bool IsExcludeAppsSupported => false;
    public bool IsIncludeAppsSupported => false;
    public bool IsLogToConsoleSupported => true;

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