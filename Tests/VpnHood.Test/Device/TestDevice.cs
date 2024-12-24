using VpnHood.Core.Client.Device;

namespace VpnHood.Test.Device;

public class TestDevice(Func<IPacketCapture> packetCaptureFactory) : IDevice
{
#pragma warning disable CS0067 // The event 'TestDevice.StartedAsService' is never used
    public event EventHandler? StartedAsService;
#pragma warning restore CS0067 // The event 'TestDevice.StartedAsService' is never used
    public string OsInfo => Environment.OSVersion + ", " + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit");
    public bool IsExcludeAppsSupported => false;
    public bool IsIncludeAppsSupported => false;
    public bool IsAlwaysOnSupported => false;
    public DeviceMemInfo? MemInfo => null;

    public DeviceAppInfo[] InstalledApps => throw new NotSupportedException();

    public Task<IPacketCapture> CreatePacketCapture(IUiContext? uiContext)
    {
        return Task.FromResult(packetCaptureFactory());
    }

    public void Dispose()
    {
    }
}