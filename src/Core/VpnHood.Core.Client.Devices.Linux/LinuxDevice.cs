using System.Runtime.InteropServices;
using VpnHood.Core.Client.Devices.Abstractions;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Core.Client.VpnServices.Abstractions.Messaging;
using VpnHood.Net.Quic.MsQuic;

namespace VpnHood.Core.Client.Devices.Linux;

public class LinuxDevice(string storageFolder) : IDevice
{
    private LinuxVpnService? _vpnService;
    public bool IsBindProcessToVpnSupported => false;
    public string OsInfo => Environment.OSVersion + ", " + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit");
    public DeviceUserAgentInfo UserAgentInfo { get; } = new() {
        Platform = $"X11; Linux {GetMachineName()}",
        Browser = DeviceUserAgentInfo.GetChromeBrowser(isMobile: false)
    };
    public string VpnServiceConfigFolder { get; } = Path.Combine(storageFolder, "vpn-service");
    public bool IsExcludeAppsSupported => false;
    public bool IsIncludeAppsSupported => false;
    public bool IsTcpProxySupported => true;
    public bool IsQuicSupported => MsQuicClient.IsSupported;
    public bool IsTv => false;

    public DeviceMemInfo MemInfo {
        get {
            var gcMemoryInfo = GC.GetGCMemoryInfo();
            return new DeviceMemInfo {
                TotalMemory = gcMemoryInfo.TotalAvailableMemoryBytes,
                AvailableMemory = gcMemoryInfo.TotalAvailableMemoryBytes - gcMemoryInfo.MemoryLoadBytes
            };
        }
    }

    public IReadOnlyList<DeviceAppInfo> InstalledApps => throw new NotSupportedException();

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

    public IMessageClient CreateMessageClient()
    {
        return new TcpMessageClient(VpnServiceConfigFolder);
    }

    public void BindProcessToVpn(bool value)
    {
        throw new NotSupportedException();
    }

    // the machine as uname names it, which Chrome on Linux puts in its user agent
    private static string GetMachineName()
    {
        return RuntimeInformation.OSArchitecture switch {
            Architecture.X64 => "x86_64",
            Architecture.Arm64 => "aarch64",
            Architecture.X86 => "i686",
            Architecture.Arm => "armv7l",
            var architecture => architecture.ToString().ToLowerInvariant()
        };
    }

    public void Dispose()
    {
        _vpnService?.Dispose();
        _vpnService = null;
    }
}