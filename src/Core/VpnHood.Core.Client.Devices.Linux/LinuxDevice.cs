using System.Runtime.InteropServices;
using VpnHood.Core.Client.Devices.Abstractions;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Messaging;
using VpnHood.Net.Quic.MsQuic;

namespace VpnHood.Core.Client.Devices.Linux;

// vpnHostPath: the VPN host the daemon starts at each connect (LinuxVpnHost), the service's and a dev run's alike.
public class LinuxDevice(string storageFolder, string vpnHostPath) : IDevice
{
    private readonly LinuxVpnHostProcess _vpnHostProcess = new(vpnHostPath, storageFolder);
    public bool IsBindProcessToVpnSupported => false;
    public string OsInfo => Environment.OSVersion + ", " + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit");
    public DeviceUserAgentInfo UserAgentInfo { get; } = new() {
        Platform = $"X11; Linux {GetMachineName()}",
        Browser = DeviceUserAgentInfo.GetChromeBrowser(isMobile: false)
    };
    public string VpnServiceConfigFolder { get; } = GetVpnServiceConfigFolder(storageFolder);
    public string VpnServiceDataFolder => VpnServiceConfigFolder; // nothing backs up a desktop app's data
    public bool IsExcludeAppsSupported => false;
    public bool IsIncludeAppsSupported => false;
    public bool IsQuicSupported => MsQuicClient.IsSupported;
    public bool IsAlwaysOnSupported => true;
    public IVpnKillSwitch KillSwitch { get; } = new LinuxVpnKillSwitch(owner: storageFolder);
    public bool IsTv => false;
    public bool IsWaitingForVpnPermission => false;

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
        return _vpnHostProcess.Start(cancellationToken);
    }

    // The VPN service's folder in the app's storage, where the VPN host finds it too.
    public static string GetVpnServiceConfigFolder(string storageFolder)
    {
        return Path.Combine(storageFolder, "vpn-service");
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
        _vpnHostProcess.Dispose();
    }
}