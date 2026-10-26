using System.IO.Pipes;
using VpnHood.AppUi.Hosting.Desktop.Platform;

namespace VpnHood.AppUi.Hosting.Desktop.Linux;

// A desktop session on Linux, by its display, which every app of the session is started with, where logind's
// XDG_SESSION_ID is not: apps the user's systemd starts lack it. Empty with no display, and then the person's alone.
public sealed class LinuxDesktopSession : IDesktopSession
{
    public string Name { get; } = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is { Length: > 0 } waylandDisplay
        ? waylandDisplay
        : Environment.GetEnvironmentVariable("DISPLAY") ?? "";

    public void HandForegroundTo(NamedPipeClientStream window)
    {
        // nothing to hand on: the window asks the desktop itself
    }
}
