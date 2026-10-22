namespace VpnHood.AppUi.Hosting.Desktop.Platform;

// What a host without a service says for itself (InProcessDesktopHost): where the window keeps things,
// how this OS tells who owns the far end of a loopback connection, and the app, which runs in the
// window's process. A platform package implements it (Desktop.WindowsVpnPlugin).
public interface IInProcessDesktopPlatform
{
    IDesktopUiPaths Paths { get; }
    ILoopbackPeerCheck PeerCheck { get; }

    // The app, built once this launch holds the window, so a second launch builds none. Disposing it
    // ends the app.
    IAppDaemonHost CreateApp();
}
