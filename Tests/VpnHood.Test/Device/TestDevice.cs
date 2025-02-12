using VpnHood.Core.Client.Device;

namespace VpnHood.Test.Device;

public class TestDevice(Func<IVpnAdapter> vpnAdapterFactory) : IDevice
{
    public string OsInfo => Environment.OSVersion + ", " + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit");
    public bool IsExcludeAppsSupported => false;
    public bool IsIncludeAppsSupported => false;
    public bool IsAlwaysOnSupported => false;
    public DeviceMemInfo? MemInfo => null;

    public DeviceAppInfo[] InstalledApps => throw new NotSupportedException();

    public Task<IVpnAdapter> CreateVpnAdapter(IUiContext? uiContext)
    {
        return Task.FromResult(vpnAdapterFactory());
    }

    public void Dispose()
    {
    }
}