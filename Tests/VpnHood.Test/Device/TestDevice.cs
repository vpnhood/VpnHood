using Microsoft.Extensions.Logging;
using VpnHood.Core.Client;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Common.Logging;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Device;

public class TestDevice(Func<IVpnAdapter> vpnAdapterFactory) : IDevice
{
    public string OsInfo => Environment.OSVersion + ", " + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit");
    public string VpnServiceSharedFolder { get; } = Path.Combine(TestHelper.WorkingPath, "VpnService-Shared", Guid.CreateVersion7().ToString());
    public bool IsExcludeAppsSupported => false;
    public bool IsIncludeAppsSupported => false;
    public bool IsAlwaysOnSupported => false;
    public DeviceMemInfo? MemInfo => null;

    public DeviceAppInfo[] InstalledApps => throw new NotSupportedException();
    public Task RequestVpnService(IUiContext? uiContext, TimeSpan timeout, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug("Test VpnService granted by TestDevice.");
        return Task.CompletedTask;
    }

    private VpnHoodClient? _vpnHoodClient;
    public Task StartVpnService(CancellationToken cancellationToken)
    {
        _vpnHoodClient?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _vpnHoodClient = VpnHoodClientFactory.Create(vpnAdapterFactory(), new TestSocketFactory(), tracker: null);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _vpnHoodClient?.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}