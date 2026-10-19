using VpnHood.AppLib.App;
using VpnHood.AppUi.Hosting.Desktop.Platform;
using VpnHood.Test.Providers;

namespace VpnHood.AppLib.Test.Daemon;

// The daemon's app, whose graceful close can be held, as one that waits on the network.
internal sealed class TestDaemonHost(VpnHoodApp app) : IAppDaemonHost
{
    public TestHold GracefulCloseHold { get; } = new();
    public bool IsQuickClosed { get; private set; }

    public async ValueTask DisposeAsync()
    {
        await GracefulCloseHold.WaitIfHeld();
        await app.DisposeAsync();
    }

    public void Dispose()
    {
        IsQuickClosed = true;
        app.Dispose();
    }
}
