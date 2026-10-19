using VpnHood.AppUi.Hosting.Desktop.Platform;

namespace VpnHood.AppLib.Test.Daemon;

// A platform's app: the daemon's, as the test builds it, which a connection never builds.
internal sealed class TestDaemonHostFactory(IAppDaemonHost? service = null) : IAppDaemonHostFactory
{
    public IAppDaemonHost CreateService() => service ?? throw new NotSupportedException();
    public IAppDaemonHost CreateDev(string storagePath) => throw new NotSupportedException();
}
