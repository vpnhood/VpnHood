using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Common.Logging;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Device;

public class TestDevice(Func<IVpnAdapter> vpnAdapterFactory, ITracker? tracker = null) : IDevice
{
    public string OsInfo => Environment.OSVersion + ", " + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit");
    public string VpnServiceConfigFolder { get; } = Path.Combine(TestHelper.WorkingPath, "VpnService", Guid.CreateVersion7().ToString());
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

    public Task StartVpnService(CancellationToken cancellationToken)
    {
        _ = cancellationToken; // ignore cancellation token to simulate outer service start
        Task.Run(SimulateStartService, CancellationToken.None);
        return Task.CompletedTask;
    }

    private VpnHoodService? _vpnHoodService;
    private async Task SimulateStartService()
    {
        if (_vpnHoodService != null) {
            VhLogger.Instance.LogDebug("Test VpnService already started.");
            return;
        }

        // create service
        var serviceContext = new VpnHoodServiceContext(VpnServiceConfigFolder);
        _vpnHoodService = await VpnHoodService.Create(serviceContext, vpnAdapterFactory(), new TestSocketFactory(), tracker: tracker);
        _vpnHoodService.Disposed += (_, _) => Dispose();

        // connect
        await _vpnHoodService.Client.Connect(CancellationToken.None);
    }

    public void Dispose()
    {
        _vpnHoodService = null;
        _vpnHoodService?.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}