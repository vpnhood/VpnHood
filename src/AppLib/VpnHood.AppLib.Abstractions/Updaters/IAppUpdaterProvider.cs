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
    // A store's own screen shows the update, and the person decides there: the check opens it by itself
    // after PromptDelay, the store's offer keeps the feed's notice away, and its "none" before a connect
    // waits for the connect. False: the notice offers the update, and nothing starts unpressed.
    bool HasOwnScreen { get; }

    // Asks the channel now, which may take the network.
    Task<AppUpdateState> Check(AppRelease? feedRelease, IUiContext? uiContext, CancellationToken cancellationToken);

    // The update as it stands, read whenever the notice refreshes: no network. With no release, what an
    // earlier one left behind goes.
    AppUpdateState GetState(AppRelease? feedRelease);

    // The person's Update, or the store's screen the check opens.
    Task StartUpdate(AppRelease? feedRelease, IUiContext? uiContext, CancellationToken cancellationToken);

    // The person's Install, where the channel stops after the download; one with no such step throws
    // NotSupportedException.
    Task CompleteUpdate(AppRelease? feedRelease, IUiContext? uiContext, CancellationToken cancellationToken);

    // Brings back the screen of an update the person started and left before it finished; false when
    // there is none. Asked with a window in front, apart from the checks' interval.
    Task<bool> ResumeUpdate(IUiContext uiContext, CancellationToken cancellationToken);
}