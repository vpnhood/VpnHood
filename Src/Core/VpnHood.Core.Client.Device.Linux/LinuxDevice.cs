using VpnHood.Core.Client.Device.UiContexts;

namespace VpnHood.Core.Client.Device.Linux;

public class LinuxDevice(string storageFolder) : IDevice
{
    private LinuxVpnService? _vpnService;
    public bool IsBindProcessToVpnSupported => false;
    public string OsInfo => Environment.OSVersion + ", " + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit");
    public string VpnServiceConfigFolder { get; } = Path.Combine(storageFolder, "vpn-service");
    public bool IsExcludeAppsSupported => false;
    public bool IsIncludeAppsSupported => false;
    public bool IsTv => false;

    public DeviceMemInfo MemInfo
    {
        get
        {
            var gcMemoryInfo = GC.GetGCMemoryInfo();
            return new DeviceMemInfo
            {
                TotalMemory = gcMemoryInfo.TotalAvailableMemoryBytes,
                AvailableMemory = gcMemoryInfo.TotalAvailableMemoryBytes - gcMemoryInfo.MemoryLoadBytes
            };
        }
    }

    public DeviceAppInfo[] InstalledApps => throw new NotSupportedException();

    public Task RequestVpnService(IUiContext? uiContext, TimeSpan timeout, CancellationToken cancellationToken)
    {
        // no need to request vpn service on windows
        return Task.CompletedTask;
    }

    public Task StartVpnService(CancellationToken cancellationToken)
    {
        if (_vpnService == null || _vpnService.IsDisposed)
            _vpnService = new LinuxVpnService(VpnServiceConfigFolder);

        _vpnService.OnConnect();
        return Task.CompletedTask;
    }

    public void BindProcessToVpn(bool value)
    {
        throw new NotSupportedException();
    }

    public void Dispose()
    {
        _vpnService?.Dispose();
        _vpnService = null;
    }
}