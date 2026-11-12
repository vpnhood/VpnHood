using VpnHood.Core.Client.Devices.Abstractions.UiContexts;

namespace VpnHood.AppLib.Abstractions.Updaters;

/// <summary>
/// The updater of the channel this copy came from: a store's, or the app's own where no store updates
/// it (the website's APK). Each finds and applies the newer version its own way; the app asks it, shows
/// what it reports and passes the person's presses on. Every call gets the feed's newer release: the
/// app's own updater takes it, a store ignores it. Each call takes the window, or null where none is
/// open, as in the desktop service: a provider that needs one throws
/// <c>UiContextNotAvailableException</c>, and the check waits for one that has it.
/// </summary>
public interface IAppUpdaterProvider
{
    /// <summary>
    /// Gets a value indicating whether a store's own screen shows the update, where the person decides: the check opens
    /// it by itself after a delay, the store's offer keeps the feed's notice away, and its "none" before a connect waits
    /// for the connect; otherwise, the app's notice offers the update, which starts at the person's press, or by itself
    /// where the channel's automatic update is on (<see cref="IsAutomaticUpdateSupported"/>).
    /// </summary>
    bool HasOwnScreen { get; }

    /// <summary>Asks the channel for an update now, which may take the network.</summary>
    /// <param name="feedRelease">The feed's newer release, or <see langword="null"/>.</param>
    /// <param name="uiContext">The app's window, or <see langword="null"/> where none is open.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the update as it stands.</returns>
    /// <exception cref="VpnHood.Core.Common.Exceptions.UiContextNotAvailableException">
    /// <paramref name="uiContext"/> is <see langword="null"/>, and the channel needs a window, as a store's.
    /// </exception>
    /// <exception cref="TimeoutException">The store did not answer its query in time, as Google Play's.</exception>
    /// <exception cref="NotSupportedException">
    /// The store needs a desktop window, which the UI context is not, as the Microsoft Store's.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<AppUpdateState> Check(AppRelease? feedRelease, IUiContext? uiContext, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the update as it stands, which the notice reads whenever it refreshes: it takes no network. With no
    /// release, what an earlier one left behind goes.
    /// </summary>
    /// <param name="feedRelease">The feed's newer release, or <see langword="null"/>.</param>
    /// <returns>The update's state; none where the channel does not update this copy, whose notice keeps the feed's link.</returns>
    AppUpdateState GetState(AppRelease? feedRelease);

    /// <summary>Starts the update: the person's Update, or the store's screen the check opens.</summary>
    /// <param name="feedRelease">The feed's newer release, or <see langword="null"/>.</param>
    /// <param name="uiContext">The app's window, or <see langword="null"/> where none is open.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// A task that completes at the channel's own stage: once the download has begun, where the app downloads the
    /// update; once the store's page is open, as the App Store's; once the store's update flow has ended, installed or
    /// dismissed, as Google Play's; or once the store's own download and install have ended, as the Microsoft Store's.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// There is no newer version to update to, the store has none to show, the store's update failed, or the release's
    /// package is not on https, as the desktop's refuses.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// This copy cannot take the release, as the website's APK refuses a package that is not on https, or the store needs
    /// a desktop window, which the UI context is not, as the Microsoft Store's.
    /// </exception>
    /// <exception cref="TimeoutException">The store did not answer its query in time, as Google Play's.</exception>
    /// <exception cref="VpnHood.Core.Common.Exceptions.UiContextNotAvailableException">
    /// <paramref name="uiContext"/> is <see langword="null"/>, and the channel needs a window, as a store's.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task StartUpdate(AppRelease? feedRelease, IUiContext? uiContext, CancellationToken cancellationToken);

    /// <summary>Installs the downloaded update: the person's Install, where the channel stops after the download.</summary>
    /// <param name="feedRelease">The feed's newer release, or <see langword="null"/>.</param>
    /// <param name="uiContext">The app's window, or <see langword="null"/> where none is open.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// A task that completes once the install has started, or, where the system must first let the app install apps
    /// (<see cref="AppUpdateState.IsInstallPermissionNeeded"/>), once the system's page for it is open.
    /// </returns>
    /// <exception cref="NotSupportedException">The channel has no such step.</exception>
    /// <exception cref="InvalidOperationException">There is no newer version, or its update is not downloaded.</exception>
    /// <exception cref="VpnHood.Core.Common.Exceptions.UiContextNotAvailableException">
    /// The channel needs a compatible UI context, as Android's, and <paramref name="uiContext"/> is
    /// <see langword="null"/> or not one.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task CompleteUpdate(AppRelease? feedRelease, IUiContext? uiContext, CancellationToken cancellationToken);

    /// <summary>
    /// Brings back the screen of an update the person started and left before it finished; the app asks it with a
    /// window in front, apart from the checks' interval.
    /// </summary>
    /// <param name="uiContext">The app's window.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is <see langword="false"/> where there is no such update.</returns>
    /// <exception cref="TimeoutException">The store did not answer its query in time, as Google Play's.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<bool> ResumeUpdate(IUiContext uiContext, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a value indicating whether an install the app runs itself, and waits on, is under way, which stops the app
    /// once it puts the new version in; the app refuses connects meanwhile.
    /// </summary>
    /// <remarks>
    /// It is a desktop's: a store's install and Android's package installer are the system's, which stops the app
    /// itself.
    /// </remarks>
    bool IsInstalling { get; }

    /// <summary>
    /// Gets a value indicating whether the app turns the channel's automatic update on and off, as a desktop's own
    /// updater does; a store's is the store's own setting.
    /// </summary>
    bool IsAutomaticUpdateSupported { get; }

    /// <summary>
    /// Gets whether the automatic update is on, read where the platform keeps it each time, as its installer and its own
    /// tools change it too.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is <see langword="true"/> where it is on.</returns>
    /// <exception cref="NotSupportedException">The channel has no such switch.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<bool> IsAutomaticUpdateOn(CancellationToken cancellationToken);

    /// <summary>
    /// Turns the automatic update on or off; off stops only the automatic update, as the person's check, Update and
    /// Install still work.
    /// </summary>
    /// <param name="isOn"><see langword="true"/> to turn it on.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once it is set.</returns>
    /// <exception cref="NotSupportedException">The channel has no such switch.</exception>
    /// <exception cref="InvalidOperationException">The platform's command failed, as Linux's systemctl.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task SetAutomaticUpdate(bool isOn, CancellationToken cancellationToken);
}