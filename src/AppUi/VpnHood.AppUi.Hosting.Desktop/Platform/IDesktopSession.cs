using System.IO.Pipes;

namespace VpnHood.AppUi.Hosting.Desktop.Platform;

// The desktop session a launch runs in, as the window's single instance asks it (DesktopUiInstance): a person has a
// window in each session, and a launch that finds it open lets it come forward.
public interface IDesktopSession
{
    // Tells one person's sessions apart; empty where there is none to tell, and then the person has one window.
    string Name { get; }

    // Whether the launch runs in Windows' sandbox, an AppContainer, which keeps the window's pipe to the app's own
    // namespace (DesktopUiInstance).
    bool IsSandboxed { get; }

    // Where only the launch the person just started may bring a window forward, hands that right to the process at
    // the pipe's far end.
    void HandForegroundTo(NamedPipeClientStream window);
}
