using VpnHood.Core.Client.Devices.Abstractions;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Messaging;
using VpnHood.Net.Quic.MsQuic;

namespace VpnHood.Core.Client.Devices.WindowsTun;

/// <summary>
/// Represents a Windows machine as the VPN app's daemon sees it, with WinTun: the VPN service runs in a VPN host process
/// the daemon starts at each connect, and the kill switch's block is the Windows Filtering Platform's
/// (<see cref="WindowsVpnKillSwitch"/>).
/// </summary>
/// <param name="storageFolder">The app's storage, with the VPN service's folder under it; the kill switch's owner.</param>
/// <param name="isDebugMode">
/// <see langword="true"/> to offer the split by app with test entries, as a debug build does.
/// </param>
/// <param name="serviceName">
/// The Windows service the daemon runs as, whose processes the kill switch's block lets out; <see langword="null"/> in a
/// run as the person (dev), whose block lets out none of its processes.
/// </param>
/// <param name="vpnHostPath">
/// The VPN host the daemon starts at each connect (<c>WindowsVpnHost</c>); <see langword="null"/> for a device that
/// starts the VPN service itself, in a head that runs both halves in its one process (<c>WindowsInProcessDevice</c>).
/// </param>
/// <remarks>
/// With a VPN host, it makes a Windows job object that ends the host with the daemon.
/// </remarks>
/// <exception cref="System.ComponentModel.Win32Exception">
/// Windows could not make or set up the VPN host's job object.
/// </exception>
public class WindowsDevice(string storageFolder, bool isDebugMode, string? serviceName = null, string? vpnHostPath = null)
    : IDevice
{
    private readonly WindowsVpnHostProcess? _vpnHostProcess = vpnHostPath is null
        ? null
        : new WindowsVpnHostProcess(vpnHostPath, storageFolder, serviceName);

    /// <inheritdoc/>
    public bool IsBindProcessToVpnSupported => false;

    /// <inheritdoc/>
    public string OsInfo => Environment.OSVersion + ", " + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit");

    /// <inheritdoc/>
    /// <remarks>As Chrome says it here, whose NT version reads 10.0 on Windows 11 too.</remarks>
    public DeviceUserAgentInfo UserAgentInfo { get; } = new() {
        Platform = $"Windows NT {Environment.OSVersion.Version.ToString(2)}" +
                   (Environment.Is64BitOperatingSystem ? "; Win64; x64" : ""),
        Browser = DeviceUserAgentInfo.GetChromeBrowser(isMobile: false)
    };

    /// <inheritdoc/>
    public string VpnServiceConfigFolder { get; } = GetVpnServiceConfigFolder(storageFolder);

    /// <inheritdoc/>
    /// <remarks>It is the same folder as the config folder.</remarks>
    public string VpnServiceDataFolder => VpnServiceConfigFolder;

    /// <inheritdoc/>
    /// <remarks>Only in debug mode, with test entries.</remarks>
    public bool IsExcludeAppsSupported => isDebugMode;

    /// <inheritdoc/>
    /// <remarks>Only in debug mode, with test entries.</remarks>
    public bool IsIncludeAppsSupported => isDebugMode;

    /// <inheritdoc/>
    public bool IsQuicSupported => MsQuicClient.IsSupported;

    /// <inheritdoc/>
    public bool IsAlwaysOnSupported => true;

    /// <inheritdoc/>
    public IVpnKillSwitch KillSwitch { get; } = new WindowsVpnKillSwitch(owner: storageFolder, serviceName);

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
    /// <remarks>In debug mode it gives 60 test entries; otherwise it throws <see cref="NotSupportedException"/>.</remarks>
    public Task<IReadOnlyList<DeviceAppInfo>> GetInstalledApps(CancellationToken cancellationToken)
    {
        return Task.FromResult(InstalledApps);
    }

    private IReadOnlyList<DeviceAppInfo> InstalledApps {
        get {
            if (!isDebugMode)
                throw new NotSupportedException();

            var list = new List<DeviceAppInfo>();
            for (var i = 0; i < 60; i++)
                list.Add(new DeviceAppInfo {
                    AppId = $"AppId {i}",
                    AppName = $"My Name {i}",
                    IconPng =
                        "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAAAXNSR0IArs4c6QAAAARzQklUCAgICHwIZIgAAAV7SURBVFiFpZdNjBzFFcd/r6pnMd7FblhjYT6SdQgXpOAx9gkhGIQwioF4TA4cYpN1gnLEIHxBfC0SEhyMWCGwhPhYIyAhFzwOUZCQjOeYm3ekcIrRLoIg2TjWLMvi2Zmu9zj0zGxPb6/XC2/U6uqq7vf/v3+9V1UjrMFOV++uCm6PYWOCxGBlAMymDW0KMuvUals/Pnn8Un3Kai/MVCuxutIrGFUgHhg0y9wH2k0zat7z+NZavfmTCXzx4K4JzA4iEg8Apg8pZr8vS6B3s6YYkzf+s/78mgjMVCtxh+ik9748GGUWP99fRCBtmDEdlbirSA2X7/h8d6U8H5hR07IFXYn4mkzEyiGxmZndt5WXjWUfTlXKMcNXzHhxceQckTicc8tlWkP0A+Nqs/4Ht31rfUmJAQXal62rq1qspgQzAobmpf85Jowl6/VktqtPYOfbj0wobAsaUDPUFFXFTDEyJFaJnlX4CpT/u/uOiQECY1Pj8bkhDs6GNGJVQ1UJpmk7aC7jwcxom9EKunSpciEEWqoshoTFoLQ06Y6n/S1VWkEPzlTKMUAEQCmaFCR+avwWXn2nweYIHB4VQy0g3iOmOBGaUYlfv/1hYXRnP9nHUHIO78E5iLwgYvhIcE5YNwQ+EkQkRoYnqTMedb+tWlfoxvUb2PHNPKOiSDCC84gaIoHRNz5g0/AIAM8cE2bOpTN4zQbj8EPK5t++j4U2Cyf24JwwdBkMX+4AQRwg0p8H8HsAZOy9P1cROdaXFuPIOw2GnOdK70irwbPlzb8RjYzw3HHHF2eX6iI79VcNG6/9QTELjEzv7YMJYC5VA5H0ZSc4bK9TqJoZvQuDI7tuQlX5LgTUoOWEaGSEiZoUgvfs/ILw6F8dIp7v/S+QyCGRg5JDIoHII94hJY9EDo181YnZWH4u/3PdelQDZsZ8CNz0wT8AOP3tUtVmwbNl/+18SlBueQ0iD5FDIp/mkXcpiS4xF7kxh+Q2mK4dHC+nZagBgJf+VRy5LWvAI0dTohJJFzwFFO8QJ9nlb6MD2ZYFtu6v5eF8JKDpcvz5N7IyeK69sNh9VzwicpEtz8rOGPxlXR7a/5s0L7oIhbKzPBeWTFi+Mg0+O4NGfyiTjD2nx8pbALj5Wit2UQA+PGRd+GQlZj3qDWdmzX4F5IeBj27dDMCT91m/LyVbHLkZvHUgnTaRvPa55RtpOjFm86B5v7cfPwTAL0e1D1IYk6VrAYDMvrACeLZLp11wVisCzebFeTrMdy7w4u+NG68uRu+BH9mvBFWuCI0Bb8vaZmBSF4Dr3z3QNGHjSltZb3pOPfAy63wJgEN/d/yvmUp81Xrj9f2pOkGVK7/6CyRzBQQy/tXmZOeJOAJQrCbwx2Ua5Phs//gJbhjexKf3PMvhh5afln734dN8fWaW0/cvXhw8dVyDboXGU+PxerFZhI2FElAwRWaUnAeD9g8tWGhjnYQz9y4CWvBVVnrm8GFMttebDqB54GgTscki0Hx+ZCumrQmLoQ2tBFNl7ya/OjiA2KRsT49lA3Wy5d2Hp/Mr49J3+TLtnv86AZtrQSfh7L2tYuAsONaQHZ/1D6cDZ8IL6ipmzGUjteyfjtyKaapoJwFV/n3XhksB/xKnlSzmAIHmgaPNYJ2KqmZTeIWFGjQJ0A5ICPxq6MylgFd70vescJuIp8bjIZK6SH6jyrSTgLUTbKHFuV0lCN9dDLyB00oefEUCPds8tW8CeAyRgeowM3SxDe2EG5KEU5UL5HOkn+1ik7Ljs4mVMFb9cxpPjcclkkmgKiIbzQzrJOnV7vD/Oxchu+n0gKGGD48VRb0mAlkbndpXJYQq7WRMO0n8p1G37fDNc4A1MGsC05jUZeeJ2qX6/BFJwTP1ncDguQAAAABJRU5ErkJggg=="
                });

            return [.. list];
        }
    }

    /// <inheritdoc/>
    /// <remarks>Windows asks nothing here: it completes at once.</remarks>
    public Task RequestVpnService(IUiContext? uiContext, TimeSpan timeout, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It starts the VPN host process, after it ends the last one if that still runs; a derived device that runs the
    /// service in its own process starts it there.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The device has no VPN host, and no override starts the service.</exception>
    public virtual Task StartVpnService(CancellationToken cancellationToken)
    {
        return _vpnHostProcess?.Start(cancellationToken) ??
               throw new InvalidOperationException("This device has no VPN host to start the VPN service in.");
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
    /// <remarks>Windows does not support it here, so it always throws <see cref="NotSupportedException"/>.</remarks>
    public void BindProcessToVpn(bool value)
    {
        throw new NotSupportedException();
    }

    /// <summary>
    /// Has the VPN host killed, if it runs, without waiting for its exit, where <paramref name="disposing"/> is set.
    /// </summary>
    /// <param name="disposing">
    /// <see langword="true"/> when called from <see cref="Dispose()"/>; <see langword="false"/> from a finalizer.
    /// </param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
            _vpnHostProcess?.Dispose();
    }

    /// <summary>
    /// Has the VPN host killed, if it runs, without waiting for its exit: the quick close, as its graceful end is its
    /// session's, which the app's disconnect asks for before this.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}