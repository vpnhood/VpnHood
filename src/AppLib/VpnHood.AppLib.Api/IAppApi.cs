using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Api.Exceptions;
using VpnHood.AppLib.Api.Ads;
using VpnHood.AppLib.Api.Sessions;
using VpnHood.AppLib.Api.Settings;
using VpnHood.AppLib.Api.SplitTunneling;
using VpnHood.AppLib.Api.Device;
using VpnHood.AppLib.Api.Updaters;

// ReSharper disable UnusedMemberInSuper.Global

namespace VpnHood.AppLib.Api;

/// <summary>
/// Represents the app's API: its information, state and settings, the VPN's connect and disconnect, the split tunneling,
/// the update, the ads and the remote access.
/// </summary>
/// <remarks>
/// A UI reaches it in the app's process or over HTTP alike (<see cref="VpnHoodApi"/>). In process, a failure comes as
/// the exception the app threw. Over HTTP, it comes as the same type where the app threw one the transport rebuilds:
/// <see cref="OperationCanceledException"/>, <see cref="TaskCanceledException"/>,
/// <see cref="VpnHood.Net.Toolkit.Exceptions.AlreadyExistsException"/>,
/// <see cref="VpnHood.Net.Toolkit.Exceptions.NotExistsException"/>, <see cref="UnauthorizedAccessException"/>,
/// <see cref="TimeoutException"/> or <see cref="InvalidOperationException"/>; any other comes as an
/// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException"/> that names it. A call that cannot reach the app fails with
/// <see cref="System.Net.Http.HttpRequestException"/>.
/// </remarks>
public interface IAppApi
{
    /// <summary>Sets what the UI gives the app: the languages it offers and its words for the app's own text.</summary>
    /// <param name="configParams">The UI's languages and words; a word it does not give keeps the app's own.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the app's information afterwards.</returns>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task<AppInfo> Configure(ConfigParams configParams, CancellationToken cancellationToken);

    /// <summary>Gets the app's information: its descriptor, features, settings, VPN profiles, state and languages.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the information.</returns>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task<AppInfo> GetInfo(CancellationToken cancellationToken);

    /// <summary>Gets the IP ranges the app's own split tunneling includes or excludes.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the ranges.</returns>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task<SplitIpsViaApp> GetSplitIpsViaApp(CancellationToken cancellationToken);

    /// <summary>Sets the IP ranges the app's own split tunneling includes or excludes, and saves them.</summary>
    /// <param name="value">The ranges.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">A range's two ends are of different families, or reversed.</exception>
    /// <exception cref="FormatException">A line is not an address, a range or a network.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    /// <remarks>The lists are written one at a time, so a list that fails leaves those before it saved.</remarks>
    Task SetSplitIpsViaApp(SplitIpsViaApp value, CancellationToken cancellationToken);

    /// <summary>Gets the IP ranges the device's VPN adapter includes or excludes.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the ranges.</returns>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task<SplitIpsViaDevice> GetSplitIpsViaDevice(CancellationToken cancellationToken);

    /// <summary>Sets the IP ranges the device's VPN adapter includes or excludes, and saves them.</summary>
    /// <param name="value">The ranges.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">A range's two ends are of different families, or reversed.</exception>
    /// <exception cref="FormatException">A line is not an address, a range or a network.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    /// <remarks>The lists are written one at a time, so a list that fails leaves those before it saved.</remarks>
    Task SetSplitIpsViaDevice(SplitIpsViaDevice value, CancellationToken cancellationToken);

    /// <summary>Gets the domains the split tunneling includes or excludes.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the domains.</returns>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task<SplitDomains> GetSplitDomains(CancellationToken cancellationToken);

    /// <summary>Sets the domains the split tunneling includes or excludes, and saves them.</summary>
    /// <param name="value">The domains.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task SetSplitDomains(SplitDomains value, CancellationToken cancellationToken);

    /// <summary>Gets the app's state as it stands, which the UI reads to refresh.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the state.</returns>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task<AppState> GetState(CancellationToken cancellationToken);

    /// <summary>Connects the VPN, ending a connection under way first.</summary>
    /// <remarks>
    /// Unless the app is installing an update, it clears the last error first, and a connect applies every pending change
    /// of the settings. A failed connect throws the app's error: the server's refusal (the app's
    /// <c>SessionException</c>), the VPN service's failure, an ad the session required that failed (the app's ad
    /// exceptions), or the system's own VPN failure, as where there is no window to ask for the VPN permission. The state
    /// keeps it as its last error, but for a refusal while the app installs an update, which leaves the last error as it
    /// was, and a connect a newer one replaced. A token canceled while the connect runs may come as
    /// <see cref="NoInternetException"/>, as the check after the failure finds no internet.
    /// </remarks>
    /// <param name="vpnProfileId">The VPN profile to connect with, or <see langword="null"/> for the current one.</param>
    /// <param name="serverLocation">The server location to connect to, or <see langword="null"/> for the profile's.</param>
    /// <param name="planId">The plan to connect on, as one a rewarded ad pays for.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once the VPN is connected.</returns>
    /// <exception cref="TimeoutException">
    /// The connection did not come up in the app's connect time, as the app's <c>ConnectionTimeoutException</c>; the VPN
    /// service did not start in time; or the system's VPN permission prompt was not answered.
    /// </exception>
    /// <exception cref="PremiumOnlyException">Always-on is for premium only, and the connect was always-on's.</exception>
    /// <exception cref="NoInternetException">A check after a failed connect found the device without internet.</exception>
    /// <exception cref="InvalidOperationException">
    /// The split by country has no IP location data, or no known country; or the client connected with no session.
    /// </exception>
    /// <exception cref="VpnHood.Net.Toolkit.Exceptions.NotExistsException">
    /// No VPN profile is given and none is set, or the profile does not exist.
    /// </exception>
    /// <exception cref="AppUpdatingException">The app is installing an update.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// A disconnect ended the connect, or the person refused the system's VPN permission, as the app's
    /// <c>UserCanceledException</c>; or <paramref name="cancellationToken"/> was canceled, over HTTP, or while the connect
    /// waited for a disconnect.
    /// </exception>
    Task Connect(Guid? vpnProfileId, string? serverLocation, ConnectPlanId planId, CancellationToken cancellationToken);

    /// <summary>Connects the VPN as <see cref="Connect"/> does, in the diagnose mode.</summary>
    /// <remarks>
    /// It checks the server's addresses and the device's internet before it connects, and the traffic through the VPN
    /// after, so a failure tells where the trouble is; it gives the connect three times as long, and logs more. Unless the
    /// app is installing an update, it clears the last error first. A failed connect throws the app's error: the server's refusal (the app's
    /// <c>SessionException</c>), the VPN service's failure, an ad the session required that failed (the app's ad
    /// exceptions), or the system's own VPN failure, as where there is no window to ask for the VPN permission. The state
    /// keeps it as its last error, but for a refusal while the app installs an update, which leaves the last error as it
    /// was, and a connect a newer one replaced. A token canceled during the internet check
    /// before the connect may come as <see cref="NoInternetException"/>.
    /// </remarks>
    /// <param name="vpnProfileId">The VPN profile to connect with, or <see langword="null"/> for the current one.</param>
    /// <param name="serverLocation">The server location to connect to, or <see langword="null"/> for the profile's.</param>
    /// <param name="planId">The plan to connect on, as one a rewarded ad pays for.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once the VPN is connected.</returns>
    /// <exception cref="TimeoutException">
    /// The connection did not come up in the app's connect time, as the app's <c>ConnectionTimeoutException</c>; the VPN
    /// service did not start in time; or the system's VPN permission prompt was not answered.
    /// </exception>
    /// <exception cref="PremiumOnlyException">Always-on is for premium only, and the connect was always-on's.</exception>
    /// <exception cref="NoStableVpnException">Traffic does not go through the connected VPN.</exception>
    /// <exception cref="NoInternetException">The device has no internet.</exception>
    /// <exception cref="InvalidOperationException">
    /// The access key names no server address or host; the split by country has no IP location data, or no known country;
    /// or the client connected with no session.
    /// </exception>
    /// <exception cref="VpnHood.Net.Toolkit.Exceptions.NotExistsException">
    /// No VPN profile is given and none is set, or the profile does not exist.
    /// </exception>
    /// <exception cref="AppUpdatingException">The app is installing an update.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// A disconnect ended the connect, or the person refused the system's VPN permission, as the app's
    /// <c>UserCanceledException</c>; or <paramref name="cancellationToken"/> was canceled, over HTTP, or while the connect
    /// waited for a disconnect.
    /// </exception>
    Task Diagnose(Guid? vpnProfileId, string? serverLocation, ConnectPlanId planId, CancellationToken cancellationToken);

    /// <summary>Disconnects the VPN.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once the disconnect has run.</returns>
    /// <exception cref="NotSupportedException">On Linux, the kill switch needs nft and cgroup v2, and the system lacks one.</exception>
    /// <exception cref="InvalidOperationException">On Windows, the kill switch's block could not be set as the disconnect leaves it.</exception>
    /// <exception cref="System.ComponentModel.Win32Exception">On Windows, the kill switch's block could not be set as the disconnect leaves it.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    /// <remarks>A VPN service that fails to stop is logged, not thrown: the task completes all the same.</remarks>
    Task Disconnect(CancellationToken cancellationToken);

    /// <summary>Clears the last error the state shows, as the person has seen it, and ends the diagnose mode's request.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task ClearLastError(CancellationToken cancellationToken);

    /// <summary>Clears the state's request to reconnect for a changed setting, as the person has seen it.</summary>
    /// <remarks>A later change that needs a reconnect raises it again.</remarks>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task ClearReconnectRequired(CancellationToken cancellationToken);

    /// <summary>Sets the person's settings, and saves them.</summary>
    /// <remarks>A change the connected VPN can take applies at once; one it cannot raises the state's request to reconnect.</remarks>
    /// <param name="userSettings">The settings.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task SetUserSettings(UserSettings userSettings, CancellationToken cancellationToken);

    /// <summary>Gets the app's log.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the log's text.</returns>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task<string> Log(CancellationToken cancellationToken);

    /// <summary>Gets the promotion image the app has saved.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the image's bytes.</returns>
    /// <exception cref="VpnHood.Net.Toolkit.Exceptions.NotExistsException">The app has no promotion image.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task<byte[]> PromotionImage(CancellationToken cancellationToken);

    /// <summary>Gets the apps installed on the device, for the split tunneling by app.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the apps.</returns>
    /// <exception cref="InvalidOperationException">Android could not list the apps.</exception>
    /// <exception cref="NotSupportedException">The device cannot list its apps, as iOS, Linux, and Windows outside a debug build.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task<IReadOnlyList<DeviceAppInfo>> GetInstalledApps(CancellationToken cancellationToken);

    /// <summary>Checks for a newer version now, as the person asked.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is what the check found.</returns>
    /// <exception cref="InvalidOperationException">The app's own updater could not check, as where a system tool would not start.</exception>
    /// <exception cref="NotSupportedException">The app has no updater.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    /// <remarks>
    /// A store with no window to show, or whose update screen fails to open, gives <see cref="VersionCheckResult.Failed"/>;
    /// a store's check that fails otherwise falls back to the feed.
    /// </remarks>
    Task<VersionCheckResult> VersionCheck(CancellationToken cancellationToken);

    /// <summary>Puts the update's notice off for a while, as the person's Later.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="NotSupportedException">The app has no updater.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task VersionCheckPostpone(CancellationToken cancellationToken);

    /// <summary>Starts the update, as the person's Update.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">No newer release is known, or its package is not on https.</exception>
    /// <exception cref="NotSupportedException">
    /// The app has no updater, or its updater has a screen of its own, a store's, or cannot update this copy, as an APK a
    /// store installed or a package not on https.
    /// </exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task StartUpdate(CancellationToken cancellationToken);

    /// <summary>Installs the downloaded update, as the person's Install.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">No newer release is known, or it is not downloaded.</exception>
    /// <exception cref="NotSupportedException">
    /// The app has no updater, or its updater has a screen of its own or cannot update this copy.
    /// </exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    /// <remarks>On Android it needs the app's window, and fails without it, as the app's <c>UiContextNotAvailableException</c>.</remarks>
    Task CompleteUpdate(CancellationToken cancellationToken);

    /// <summary>Gets whether the app's automatic update is on.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is <see langword="true"/> where it is on.</returns>
    /// <exception cref="InvalidOperationException">The system's tool could not be run.</exception>
    /// <exception cref="NotSupportedException">The app has no updater, or its updater has no switch of its own, as only a desktop's has.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task<bool> GetAutomaticUpdate(CancellationToken cancellationToken);

    /// <summary>Turns the app's automatic update on or off.</summary>
    /// <param name="value"><see langword="true"/> to turn it on.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">The system's tool could not be run, or it refused the change.</exception>
    /// <exception cref="NotSupportedException">The app has no updater, or its updater has no switch of its own, as only a desktop's has.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task SetAutomaticUpdate(bool value, CancellationToken cancellationToken);

    /// <summary>Shows a rewarded ad, which extends the connected session.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once the ad's reward is settled.</returns>
    /// <exception cref="InvalidOperationException">
    /// The app shows no rewarded ads, the VPN is not connected, the session cannot be extended now, or an ad is showing
    /// already.
    /// </exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    /// <remarks>
    /// An ad that fails comes as the app's ad exceptions: without the app's window, its <c>ShowAdNoUiException</c>; then its
    /// <c>LoadAdException</c>, its <c>ShowAdException</c>, or its <c>AdBlockerException</c> where the device's private DNS
    /// may block ads. A failure of the VPN service comes as its own.
    /// </remarks>
    Task ExtendByRewardedAd(CancellationToken cancellationToken);

    /// <summary>Shows the ad network's privacy options, for the person to change their consent.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">The app has no consent provider.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    /// <remarks>
    /// It needs the app's window, and fails without it, as the app's <c>UiContextNotAvailableException</c>; a form that
    /// fails comes as the app's <c>AdException</c>.
    /// </remarks>
    Task ShowAdPrivacyOptions(CancellationToken cancellationToken);

    /// <summary>Records the person's rating of the app.</summary>
    /// <param name="userReview">The rating, with the person's text.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task SetUserReview(AppUserReview userReview, CancellationToken cancellationToken);

    /// <summary>Gets the countries the split tunneling by country can take, by their codes.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the countries' codes.</returns>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task<IReadOnlyList<string>> GetSupportedSplitCountries(CancellationToken cancellationToken);

    /// <summary>Tells the app how the UI's own ad ended.</summary>
    /// <param name="result">How the ad ended.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task InternalAdDismiss(ShowAdResult result, CancellationToken cancellationToken);

    /// <summary>Tells the app that the UI's own ad failed.</summary>
    /// <param name="errorMessage">Why it failed.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task InternalAdError(string errorMessage, CancellationToken cancellationToken);

    /// <summary>Gets the remote access's state, as the pairing screen polls it.</summary>
    /// <remarks>Where the remote access is on, it binds again where the device's addresses changed; it starts nothing that is off.</remarks>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the state.</returns>
    /// <exception cref="ObjectDisposedException">The web host was disposed.</exception>
    /// <exception cref="InvalidOperationException">The web host has no web content, or nothing could bind, as with no LAN address.</exception>
    /// <exception cref="UnauthorizedAccessException">
    /// The call came over HTTP from another device: the remote access is controlled from the device itself.
    /// </exception>
    /// <exception cref="NotSupportedException">The app was given no web host.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task<RemoteAccessState> GetRemoteAccess(CancellationToken cancellationToken);

    /// <summary>Starts the remote access, for a phone to pair, and keeps the screen on meanwhile where the device's screen sleeps.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the state afterwards.</returns>
    /// <exception cref="ObjectDisposedException">The web host was disposed.</exception>
    /// <exception cref="InvalidOperationException">The web host has no web content, or nothing could bind, as with no LAN address.</exception>
    /// <exception cref="UnauthorizedAccessException">
    /// The call came over HTTP from another device: the remote access is controlled from the device itself.
    /// </exception>
    /// <exception cref="NotSupportedException">The app was given no web host.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task<RemoteAccessState> StartRemoteAccess(CancellationToken cancellationToken);

    /// <summary>Stops the remote access, and lets the screen sleep again.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="UnauthorizedAccessException">
    /// The call came over HTTP from another device: the remote access is controlled from the device itself.
    /// </exception>
    /// <exception cref="NotSupportedException">The app was given no web host.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task StopRemoteAccess(CancellationToken cancellationToken);
}