using Avalonia.Controls;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Api.App;
using VpnHood.AppUi.Common;
using VpnHood.AppUi.Hosting.Desktop.Abstractions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Avalonia.Desktop;

// The connection's badge on the window's taskbar button, the one the tray shows on its icon: Windows' overlay icon,
// which the host draws (AvaloniaTaskbarOverlay); or, in a sandboxed app, which Windows refuses the overlay, the
// shell's (IDesktopShell.ShowConnectionBadge), Windows' own badge. None on Linux, whose docks show no such thing. It
// follows the state as the tray does (AvaloniaAppStateFollower) and is set again only when the badge or its words
// change. A badge that cannot be shown is logged and the UI goes on. On the UI thread.
internal sealed class AvaloniaTaskbarBadge : IDisposable
{
    private readonly AvaloniaAppStateFollower _stateFollower;
    private readonly IDesktopShell? _shell;
    private readonly AvaloniaTaskbarOverlay? _overlay;
    private (DesktopConnectionBadge Badge, string Text)? _shown;
    private bool _disposed;

    private AvaloniaTaskbarBadge(AvaloniaAppStateFollower stateFollower, IDesktopShell? shell,
        AvaloniaTaskbarOverlay? overlay)
    {
        _stateFollower = stateFollower;
        _shell = shell;
        _overlay = overlay;
        _stateFollower.StateRead += StateFollower_StateRead;
    }

    // None where there is no taskbar button to show it on, or Windows' taskbar cannot be reached.
    public static AvaloniaTaskbarBadge? Create(Window window, AvaloniaAppStateFollower stateFollower, IDesktopShell? shell)
    {
        if (shell != null)
            return new AvaloniaTaskbarBadge(stateFollower, shell, overlay: null);

        if (!OperatingSystem.IsWindows())
            return null;

        var overlay = TryCreateOverlay(window);
        return overlay == null ? null : new AvaloniaTaskbarBadge(stateFollower, shell: null, overlay);
    }

    private static AvaloniaTaskbarOverlay? TryCreateOverlay(Window window)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        try {
            return new AvaloniaTaskbarOverlay(window);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not reach the taskbar, so its button shows no connection badge.");
            return null;
        }
    }

    private void StateFollower_StateRead(object? sender, AppState state)
    {
        if (_disposed)
            return;

        var shown = (Badge: AvaloniaConnectionBadge.ForState(state), Text: Strings.Current.ConnectionStateText(state.ConnectionState));
        if (_shown == shown)
            return;

        _shown = shown;
        try {
            Show(shown.Badge, shown.Text);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not show the connection's badge on the taskbar button.");
        }
    }

    private void Show(DesktopConnectionBadge badge, string text)
    {
        if (_shell != null)
            _shell.ShowConnectionBadge(badge);
        else if (_overlay != null && OperatingSystem.IsWindows())
            _overlay.SetIcon(AvaloniaConnectionBadge.DrawAlone(badge, _overlay.IconSize), text);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _stateFollower.StateRead -= StateFollower_StateRead;

        // the shell's badge is the app's, not the window's: it stays on a pinned button once the UI has ended
        try {
            _shell?.ShowConnectionBadge(null);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not take the connection's badge off the taskbar button.");
        }

        if (OperatingSystem.IsWindows())
            _overlay?.Dispose();
    }
}
