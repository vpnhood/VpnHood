using VpnHood.AppLib.Abstractions.Device;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;

namespace VpnHood.AppLib.Abstractions.Device;

/// <summary>
/// Represents what the app's UI asks of the device: the system's settings pages, its permissions, its share sheet,
/// and what it tells of its proxy, private DNS and system bars.
/// </summary>
/// <remarks>
/// Each feature has its <c>Is...Supported</c> flag, which the UI reads before it offers the feature; the request or the
/// page behind a feature the device does not support throws <see cref="NotSupportedException"/>. The readings answer
/// <see langword="null"/> or the defaults where the device cannot tell, and <see cref="KeepScreenOn"/> may do nothing, as
/// where it is not supported.
/// </remarks>
public interface IDeviceUiProvider
{
    /// <summary>
    /// Gets a value indicating whether the device offers the app's Quick Launch: a shortcut in the system's own UI, as
    /// Android's Quick Settings tile, that connects the VPN.
    /// </summary>
    bool IsQuickLaunchSupported { get; }

    /// <summary>Gets a value indicating whether the app can ask the system to add its Quick Launch for the person.</summary>
    bool IsRequestQuickLaunchSupported { get; }

    /// <summary>Asks the system to add the app's Quick Launch, which the person accepts or refuses.</summary>
    /// <param name="uiContext">The app's UI context.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is <see langword="true"/> where the Quick Launch is added, or was already.</returns>
    /// <exception cref="NotSupportedException">The device cannot ask for it.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <remarks>
    /// Android's throws its <c>RequestQuickLaunchException</c> where the system answers no within a second of the
    /// request.
    /// </remarks>
    Task<bool> RequestQuickLaunch(IUiContext uiContext, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a value indicating whether the app may show notifications, or <see langword="null"/> where the device does
    /// not say.
    /// </summary>
    bool? IsNotificationEnabled { get; }

    /// <summary>Gets a value indicating whether the app can ask the person to let it show notifications.</summary>
    bool IsRequestNotificationSupported { get; }

    /// <summary>Asks the person to let the app show notifications.</summary>
    /// <param name="uiContext">The app's UI context.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is <see langword="true"/> where the app may show them now.</returns>
    /// <exception cref="NotSupportedException">The device cannot ask for it.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<bool> RequestNotification(IUiContext uiContext, CancellationToken cancellationToken);

    /// <summary>
    /// Whether a page can be handed to an EXTERNAL browser. Not "can this app draw a page" — the UI can
    /// always render a page inside itself; this is only about leaving the app. False on a device
    /// with no browser, which is where an outbound link opens nothing at all: the UI withholds the
    /// link rather than offering a dead one.
    /// </summary>
    bool IsWebBrowserSupported { get; }

    /// <summary>Gets a value indicating whether the device has a private DNS setting, which can take the DNS past the VPN.</summary>
    bool IsPrivateDnsSettingsSupported { get; }

    /// <summary>Gets a value indicating whether the app can open the system's settings.</summary>
    bool IsSettingsSupported { get; }

    /// <summary>Opens the system's settings.</summary>
    /// <param name="uiContext">The app's UI context.</param>
    /// <exception cref="NotSupportedException">The device cannot open them.</exception>
    void OpenSettings(IUiContext uiContext);

    /// <summary>Gets a value indicating whether the app can open the system's always-on VPN setting.</summary>
    bool IsAlwaysOnSettingsSupported { get; }

    /// <summary>Opens the system's VPN settings, where the person turns always-on VPN on for the app.</summary>
    /// <param name="uiContext">The app's UI context.</param>
    /// <exception cref="NotSupportedException">The device cannot open them.</exception>
    void OpenAlwaysOnSettings(IUiContext uiContext);

    /// <summary>
    /// Gets a value indicating whether the app can open the system's kill switch setting, which blocks the connections
    /// made without the VPN.
    /// </summary>
    bool IsKillSwitchSettingsSupported { get; }

    /// <summary>Opens the system's VPN settings, where the person turns the system's kill switch on for the app.</summary>
    /// <param name="requiredContext">The app's UI context.</param>
    /// <exception cref="NotSupportedException">The device cannot open them.</exception>
    void OpenKillSwitchSettings(IUiContext requiredContext);

    /// <summary>Gets a value indicating whether the app can open its own page in the system's settings.</summary>
    bool IsAppSettingsSupported { get; }

    /// <summary>Opens the app's own page in the system's settings.</summary>
    /// <param name="context">The app's UI context.</param>
    /// <exception cref="NotSupportedException">The device cannot open it.</exception>
    void OpenAppSettings(IUiContext context);

    /// <summary>Gets a value indicating whether the app can open its notification settings in the system's settings.</summary>
    bool IsAppNotificationSettingsSupported { get; }

    /// <summary>Opens the app's notification settings in the system's settings.</summary>
    /// <param name="uiContext">The app's UI context.</param>
    /// <exception cref="NotSupportedException">The device cannot open them.</exception>
    void OpenAppNotificationSettings(IUiContext uiContext);

    /// <summary>
    /// Whether the device has a share sheet, which hands a file to an app the user picks. False on a
    /// TV and a computer, where the UI copies the text instead.
    /// </summary>
    bool IsShareSupported { get; }

    /// <summary>Hands a file to an app the person picks in the device's share sheet.</summary>
    /// <param name="uiContext">The app's UI context.</param>
    /// <param name="fileName">The file's name.</param>
    /// <param name="content">The file's content.</param>
    /// <exception cref="NotSupportedException">The device has no share sheet.</exception>
    /// <exception cref="InvalidOperationException">There is no window to share from, as iOS's finds none.</exception>
    void ShareFile(IUiContext uiContext, string fileName, byte[] content);

    /// <summary>Gets a value indicating whether the device can tell its proxy settings.</summary>
    bool IsProxySettingsSupported { get; }

    /// <summary>Gets the device's proxy settings.</summary>
    /// <returns>The settings, or <see langword="null"/> where the device has no proxy or cannot tell it.</returns>
    DeviceProxySettings? GetProxySettings();

    /// <summary>Gets the device's private DNS, which can take the DNS past the VPN.</summary>
    /// <returns>The private DNS, or <see langword="null"/> where the device cannot tell it.</returns>
    PrivateDns? GetPrivateDns();

    /// <summary>Gets the heights of the system bars over the app's window, for its layout to leave room.</summary>
    /// <param name="uiContext">The app's UI context.</param>
    /// <returns>The bars' heights; none where the device cannot tell them.</returns>
    SystemBarsInfo GetBarsInfo(IUiContext uiContext);

    /// <summary>
    /// Holds off the screen's idle sleep while the app is in front, for a page that is meant to stay up
    /// with nobody at the remote. An implementation that does not support it, as the Windows one, does nothing.
    /// </summary>
    /// <param name="uiContext">The app's UI context.</param>
    /// <param name="value"><see langword="true"/> to keep the screen on; <see langword="false"/> to let it sleep again.</param>
    /// <returns>A task that completes once it is set.</returns>
    Task KeepScreenOn(IUiContext uiContext, bool value);
}