using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Client.Device.UiContexts;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Tunneling.Sockets;
using VpnHood.Core.VpnAdapters.Abstractions;

namespace VpnHood.Test.Device;

public class TestDevice(
    TestHelper testHelper, 
    Func<VpnAdapterSettings, IVpnAdapter> vpnAdapterFactory) : 
    IDevice
{
    private readonly CancellationTokenSource _disposeCancellationTokenSource = new();
    public TestVpnService? VpnService { get; private set; }
    public ISocketFactory SocketFactory => new TestDeviceSocketFactory(this);
    public int BindProcessToVpnFalseCount { get; private set; }
    public int BindProcessToVpnTrueCount { get; private set; }
    public bool LastBindProcessToVpnValue { get; private set; }
    public bool IsBindProcessToVpnSupported => true;
    public string OsInfo => Environment.OSVersion + ", " + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit");
    public string VpnServiceConfigFolder { get; } = Path.Combine(testHelper.WorkingPath, "VpnService");
    public bool IsTv => false;
    public bool IsExcludeAppsSupported => false;
    public bool IsIncludeAppsSupported => false;
    public bool IsAlwaysOnSupported => false;
    public DeviceMemInfo? MemInfo => null;
    public TimeSpan StartServiceDelay { get; set; } = TimeSpan.Zero;

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

    public void BindProcessToVpn(bool value)
    {
        LastBindProcessToVpnValue = value;
        if (value)
            BindProcessToVpnTrueCount++;
        else
            BindProcessToVpnFalseCount++;
    }

    private async Task SimulateStartService()
    {
        // delay start
        await Task.Delay(StartServiceDelay, _disposeCancellationTokenSource.Token);
        _disposeCancellationTokenSource.Token.ThrowIfCancellationRequested();

        // create service
        if (VpnService == null || VpnService.IsDisposed)
            VpnService = new TestVpnService(VpnServiceConfigFolder, vpnAdapterFactory);

        VpnService.OnConnect();
    }

    public async ValueTask DisposeAsync()
    {
        if (VpnService != null)
            await VpnService.DisposeAsync();

        VpnService = null;
    }

}