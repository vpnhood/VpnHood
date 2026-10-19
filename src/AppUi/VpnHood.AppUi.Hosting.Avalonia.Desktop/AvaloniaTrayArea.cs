using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Avalonia.Desktop;

// Whether the desktop shows tray icons. Windows always does. A Linux desktop does where something
// owns the StatusNotifierWatcher name on the session bus - KDE, GNOME with Ubuntu's AppIndicator
// extension - and plain GNOME does not. Avalonia's tray registers its icon with that same name
// (DBusTrayIconImpl), so this is the answer the icon itself gets. Watched while the UI runs, since a
// panel that restarts or an extension switched off changes it. It cannot see a KDE panel whose
// system tray widget a person removed: kded keeps the name and plasmashell its host
// (IsStatusNotifierHostRegistered stays true), so a window hidden there comes back only with a
// second launch. The bus is Avalonia's own D-Bus library (Tmds.DBus.Protocol, through
// Avalonia.FreeDesktop), so it moves with Avalonia's version.
internal sealed class AvaloniaTrayArea : IDisposable
{
    private const string StatusNotifierWatcherName = "org.kde.StatusNotifierWatcher";
    private readonly NameOwnerWatcher? _watcher;

    private AvaloniaTrayArea(NameOwnerWatcher? watcher)
    {
        _watcher = watcher;
    }

    public static AvaloniaTrayArea Watch()
    {
        return new AvaloniaTrayArea(OperatingSystem.IsLinux() ? TryWatchLinux() : null);
    }

    // No session bus - a session without D-Bus - is a desktop that shows no tray icons.
    private static NameOwnerWatcher? TryWatchLinux()
    {
        try {
            return DBusConnection.Session.WatchNameOwnerAsync(StatusNotifierWatcherName).GetAwaiter().GetResult();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Could not reach the session bus, so the window has no tray icon.");
            return null;
        }
    }

    public bool IsShown => OperatingSystem.IsWindows() || _watcher?.GetCurrentOwner() != null;

    // Whether the desktop shows tray icons before the token is cancelled: a sign-in starts the UI as
    // the desktop starts, and its panel may answer a moment later.
    public async Task<bool> WaitShownAsync(CancellationToken cancellationToken)
    {
        if (IsShown || _watcher == null)
            return IsShown;

        try {
            await _watcher.WaitForOwnerAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            return false;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Could not wait for the desktop's tray.");
            return false;
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
    }
}
