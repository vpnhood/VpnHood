using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Messaging;

namespace VpnHood.Core.Client.Devices.Abstractions;

/// <summary>
/// Defines a device as the VPN app sees it: what it supports, where its VPN service keeps its files, and how the app
/// asks for the VPN service, starts it and talks to it.
/// </summary>
public interface IDevice : IDisposable
{
    /// <summary>
    /// Gets the folder of the VPN service's own files: its options, status and log, and the split filters the app
    /// publishes for it, meant to stay on this device, out of backups and transfers to a new device.
    /// </summary>
    /// <remarks>
    /// Where the device can, the folder is kept out of the OS's backups: Android's is its folder outside them, and on
    /// iOS a failure to exclude it is only logged. A desktop's is an ordinary folder.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Android gives no folder outside its backups.</exception>
    string VpnServiceConfigFolder { get; }

    /// <summary>
    /// Gets the folder of what the app and the VPN service share that is the person's own, as the proxies they added,
    /// meant to travel with a backup where the OS backs it up; tvOS keeps it among its caches.
    /// </summary>
    string VpnServiceDataFolder { get; }

    /// <summary>Gets a value indicating whether the device is a TV.</summary>
    bool IsTv { get; }

    /// <summary>Gets a value indicating whether the device can keep chosen apps out of the tunnel.</summary>
    bool IsExcludeAppsSupported { get; }

    /// <summary>Gets a value indicating whether the device can let only chosen apps into the tunnel.</summary>
    bool IsIncludeAppsSupported { get; }

    /// <summary>Gets a value indicating whether <see cref="BindProcessToVpn"/> works on the device.</summary>
    bool IsBindProcessToVpnSupported { get; }

    /// <summary>Gets a value indicating whether the device can make QUIC connections.</summary>
    bool IsQuicSupported { get; }

    /// <summary>
    /// Gets a value indicating whether the app can keep the VPN always on itself, which its always-on setting then
    /// turns on, as a desktop's service that starts at boot does; <see langword="false"/> where the system does it, as
    /// on Android, whose setting the app only opens.
    /// </summary>
    bool IsAlwaysOnSupported { get; }

    /// <summary>
    /// Gets the kill switch's block, where the device has one, as a desktop's service; otherwise,
    /// <see langword="null"/>. The device holds it, as it outlives each connection's VPN service, whose host controls
    /// the same block through a controller of its own.
    /// </summary>
    IVpnKillSwitch? KillSwitch { get; }

    /// <summary>Gets a description of the device and its OS, as <c>Google: Pixel 8, Android: 16</c>.</summary>
    string OsInfo { get; }

    /// <summary>Gets the parts of a browser's user agent on the device, which the app builds its own from.</summary>
    DeviceUserAgentInfo UserAgentInfo { get; }

    /// <summary>
    /// Gets the device's memory as the platform reports it now, or <see langword="null"/> where it is not known; a
    /// desktop's comes from the .NET garbage collector's view.
    /// </summary>
    DeviceMemInfo? MemInfo { get; }

    /// <summary>Gets the installed apps a split by app can pick from, each with its name and icon.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the apps: on Android, by name; a Windows debug build gives test entries.</returns>
    /// <exception cref="NotSupportedException">The device cannot split by app.</exception>
    /// <exception cref="InvalidOperationException">Android's package manager or an app's resources are not available.</exception>
    /// <exception cref="Exception">Android could not encode an app's icon.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<IReadOnlyList<DeviceAppInfo>> GetInstalledApps(CancellationToken cancellationToken);

    /// <summary>
    /// Gets a value indicating whether <see cref="RequestVpnService"/> waits on the OS's prompt for the person's yes,
    /// so a UI elsewhere, as a browser driving a TV, can say where the connection waits.
    /// </summary>
    bool IsWaitingForVpnPermission { get; }

    /// <summary>
    /// Prepares the device to run the app's VPN service: on Android, it asks for the person's consent with the OS's
    /// prompt where the OS requires it now; on the Windows VPN plug-in, it prepares the VPN profile; elsewhere it may do
    /// nothing, as iOS configures the VPN when it starts.
    /// </summary>
    /// <param name="uiContext">
    /// The UI to show a prompt in, or <see langword="null"/>; on Android, one given must be Android's own, and one is
    /// needed where the OS must ask.
    /// </param>
    /// <param name="timeout">How long to wait for the person's answer, where the device waits for one, as Android does.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once the device is prepared.</returns>
    /// <exception cref="VpnHood.Core.Client.Abstractions.Exceptions.UserCanceledException">The person refused, on Android.</exception>
    /// <exception cref="Exception">Android must ask, and no UI was given.</exception>
    /// <exception cref="InvalidCastException">The UI given is not Android's own, on Android.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="timeout"/> is negative, other than -1 ms, or too long, on Android.
    /// </exception>
    /// <exception cref="TimeoutException">
    /// The person did not answer in time, or the wait was canceled, on Android; or the Windows VPN plug-in's profile did
    /// not disconnect in time.
    /// </exception>
    /// <exception cref="InvalidOperationException">The Windows VPN plug-in's profile could not be prepared.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task RequestVpnService(IUiContext? uiContext, TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>
    /// Starts the VPN service, which then connects as its options in the config folder say. The task completes once
    /// the start is under way, not when the VPN is up; the app's service manager waits for that.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <remarks>iOS fails it with an <c>NSErrorException</c> of its own.</remarks>
    /// <exception cref="VpnHood.Core.Client.VpnServices.Abstractions.Exceptions.VpnServiceException">
    /// Android could not start the service.
    /// </exception>
    /// <exception cref="InvalidOperationException">A desktop's VPN host or the Windows VPN plug-in's profile is missing or failed.</exception>
    /// <exception cref="ObjectDisposedException">A desktop's VPN host process was disposed.</exception>
    /// <exception cref="TimeoutException">The Windows VPN plug-in did not start in time.</exception>
    /// <exception cref="System.ComponentModel.Win32Exception">Windows refused the VPN host's process.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task StartVpnService(CancellationToken cancellationToken);

    /// <summary>Creates a client of the VPN service's messages, through which the app commands it.</summary>
    /// <returns>The client.</returns>
    IMessageClient CreateMessageClient();

    /// <summary>
    /// Binds the app's own connections to the system's default network, which is the VPN while one is connected, or,
    /// with <see langword="false"/>, to a network outside the VPN: on Android, the first of Ethernet, Wi-Fi, USB,
    /// satellite, Bluetooth and cellular.
    /// </summary>
    /// <param name="value">
    /// <see langword="true"/> for the system's default network; <see langword="false"/> for a network outside the VPN.
    /// </param>
    /// <exception cref="NotSupportedException">The device does not support it (<see cref="IsBindProcessToVpnSupported"/>).</exception>
    /// <exception cref="Exception">Android found no network outside the VPN.</exception>
    void BindProcessToVpn(bool value);
}