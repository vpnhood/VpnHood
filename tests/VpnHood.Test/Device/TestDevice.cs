using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Devices.Abstractions;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Core.Client.VpnServices.Abstractions.Messaging;
using VpnHood.Core.Client.VpnServices.Host;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Sockets;
using VpnHood.Net.VpnAdapters.Abstractions;

namespace VpnHood.Test.Device;

public class TestDevice(
    TestHelper testHelper,
    Func<VpnAdapterSettings, IVpnAdapter> vpnAdapterFactory) 
    : IDevice
{
    private readonly CancellationTokenSource _disposeCancellationTokenSource = new();
    public TestVpnService? VpnService { get; private set; }
    public ISocketFactory SocketFactory => new TestDeviceSocketFactory(this);
    public int BindProcessToVpnFalseCount { get; private set; }
    public int BindProcessToVpnTrueCount { get; private set; }
    public bool LastBindProcessToVpnValue { get; private set; }
    public bool IsTcpProxySupported => true;
    public bool IsQuicSupported => SocketFactory.IsQuicSupported;
    public bool IsBindProcessToVpnSupported => true;
    public string OsInfo => Environment.OSVersion + ", " + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit");
    public DeviceUserAgentInfo UserAgentInfo { get; init; } = new() {
        Platform = "X11; Linux x86_64",
        Browser = DeviceUserAgentInfo.GetChromeBrowser(isMobile: false)
    };
    public string VpnServiceConfigFolder { get; } = Path.Combine(testHelper.WorkingPath, "VpnService");
    public string VpnServiceDataFolder => VpnServiceConfigFolder;
    public bool IsTv => false;
    public bool IsExcludeAppsSupported => false;
    public bool IsIncludeAppsSupported => false;
    public bool IsAlwaysOnSupported { get; init; } // a desktop's, where a test plays one
    public DeviceMemInfo? MemInfo => null;
    public TimeSpan StartServiceDelay { get; set; } = TimeSpan.Zero;

    // the machine's one tunnel, where a test plays a desktop
    public IVpnTunnelLock? TunnelLock { get; init; }

    // the VPN permission's ask, where a test plays the person: the answer may come late, past the start's cancel
    public Func<CancellationToken, Task>? RequestVpnServiceHandler { get; init; }

    public IReadOnlyList<DeviceAppInfo> InstalledApps => throw new NotSupportedException();

    public Task RequestVpnService(IUiContext? uiContext, TimeSpan timeout, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug("Test VpnService granted by TestDevice.");
        return RequestVpnServiceHandler?.Invoke(cancellationToken) ?? Task.CompletedTask;
    }

    public Task StartVpnService(CancellationToken cancellationToken)
    {
        _ = cancellationToken; // ignore cancellation token to simulate outer service start
        Task.Run(SimulateStartService, CancellationToken.None);
        return Task.CompletedTask;
    }

    public IMessageClient CreateMessageClient()
    {
        return new TcpMessageClient(VpnServiceConfigFolder);
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
            VpnService = new TestVpnService(VpnServiceConfigFolder, testHelper.ClientNetFilter, vpnAdapterFactory,
                TunnelLock);

        VpnService.OnConnect();
    }

    public void Dispose()
    {
        VpnService?.Dispose();
        VpnService = null;
    }
}