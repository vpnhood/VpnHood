using System.Runtime.InteropServices;
using VpnHood.Core.Client.Devices.Abstractions;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Messaging;
using VpnHood.Net.Quic.MsQuic;

namespace VpnHood.Core.Client.Devices.Linux;

/// <summary>
/// Represents a Linux machine as the VPN app's daemon sees it: the VPN service runs in a VPN host process the daemon
/// starts at each connect, and the kill switch's block is an nftables table (<see cref="LinuxVpnKillSwitch"/>).
/// </summary>
/// <param name="storageFolder">The app's storage, with the VPN service's folder under it; the kill switch's owner.</param>
/// <param name="vpnHostPath">
/// The VPN host the daemon starts at each connect (<c>LinuxVpnHost</c>), the service's and a dev run's alike.
/// </param>
public class LinuxDevice(string storageFolder, string vpnHostPath) : IDevice
{
    private readonly LinuxVpnHostProcess _vpnHostProcess = new(vpnHostPath, storageFolder);

    /// <inheritdoc/>
    public bool IsBindProcessToVpnSupported => false;

    /// <inheritdoc/>
    public string OsInfo => Environment.OSVersion + ", " + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit");

    /// <inheritdoc/>
    /// <remarks>As Chrome on Linux says it: <c>X11; Linux</c> and the machine as <c>uname</c> names it.</remarks>
    public DeviceUserAgentInfo UserAgentInfo { get; } = new() {
        Platform = $"X11; Linux {GetMachineName()}",
        Browser = DeviceUserAgentInfo.GetChromeBrowser(isMobile: false)
    };

    /// <inheritdoc/>
    public string VpnServiceConfigFolder { get; } = GetVpnServiceConfigFolder(storageFolder);

    /// <inheritdoc/>
    /// <remarks>It is the same folder as the config folder.</remarks>
    public string VpnServiceDataFolder => VpnServiceConfigFolder;

    /// <inheritdoc/>
    public bool IsExcludeAppsSupported => false;

    /// <inheritdoc/>
    public bool IsIncludeAppsSupported => false;

    /// <inheritdoc/>
    public bool IsQuicSupported => MsQuicClient.IsSupported;

    /// <inheritdoc/>
    public bool IsAlwaysOnSupported => true;

    /// <inheritdoc/>
    public IVpnKillSwitch KillSwitch { get; } = new LinuxVpnKillSwitch(owner: storageFolder);

    /// <inheritdoc/>
    public bool IsTv => false;

    /// <inheritdoc/>
    public bool IsWaitingForVpnPermission => false;

    /// <inheritdoc/>
    public DeviceMemInfo MemInfo {
        get {
            var gcMemoryInfo = GC.GetGCMemoryInfo();
            return new DeviceMemInfo {
                TotalMemory = gcMemoryInfo.TotalAvailableMemoryBytes,
                AvailableMemory = gcMemoryInfo.TotalAvailableMemoryBytes - gcMemoryInfo.MemoryLoadBytes
            };
        }
    }

    /// <inheritdoc/>
    /// <remarks>Linux cannot split by app, so it always throws <see cref="NotSupportedException"/>.</remarks>
    public Task<IReadOnlyList<DeviceAppInfo>> GetInstalledApps(CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    /// <remarks>Linux asks nothing: it completes at once.</remarks>
    public Task RequestVpnService(IUiContext? uiContext, TimeSpan timeout, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It starts the VPN host process, after it ends the last one if that still runs, as one at a time may hold the
    /// service's folder.
    /// </remarks>
    /// <exception cref="System.ComponentModel.Win32Exception">The VPN host could not be started.</exception>
    public Task StartVpnService(CancellationToken cancellationToken)
    {
        return _vpnHostProcess.Start(cancellationToken);
    }

    /// <summary>Gets the VPN service's folder in the app's storage, where the VPN host finds it too.</summary>
    /// <param name="storageFolder">The app's storage.</param>
    /// <returns>The folder: <c>vpn-service</c> under <paramref name="storageFolder"/>.</returns>
    public static string GetVpnServiceConfigFolder(string storageFolder)
    {
        return Path.Combine(storageFolder, "vpn-service");
    }

    /// <inheritdoc/>
    /// <remarks>It is a loopback TCP client (<see cref="TcpMessageClient"/>).</remarks>
    public IMessageClient CreateMessageClient()
    {
        return new TcpMessageClient(VpnServiceConfigFolder);
    }

    /// <inheritdoc/>
    /// <remarks>Linux does not support it, so it always throws <see cref="NotSupportedException"/>.</remarks>
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

    /// <summary>
    /// Has the VPN host killed, if it runs, without waiting for its exit; a failure is logged. It is the quick close, as
    /// the VPN host's graceful end is its session's, which the app's disconnect asks for before this.
    /// </summary>
    public void Dispose()
    {
        _vpnHostProcess.Dispose();
    }
}