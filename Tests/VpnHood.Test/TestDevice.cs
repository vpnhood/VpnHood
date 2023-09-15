using System;
using System.Threading.Tasks;
using VpnHood.Client.Device;

namespace VpnHood.Test;

internal class TestDevice : IDevice
{
    private readonly TestDeviceOptions _options;

    public TestDevice(TestDeviceOptions? options = default)
    {
        _options = options ?? new TestDeviceOptions();
    }
#pragma warning disable 0067
    public event EventHandler? OnStartAsService;
#pragma warning restore 0067

    public string OperatingSystemInfo =>
        Environment.OSVersion + ", " + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit");

    public bool IsExcludeAppsSupported => false;

    public bool IsIncludeAppsSupported => false;

    public DeviceAppInfo[] InstalledApps => throw new NotSupportedException();

    public Task<IPacketCapture> CreatePacketCapture()
    {
        var res = new TestPacketCapture(_options);
        return Task.FromResult((IPacketCapture) res);
    }
}