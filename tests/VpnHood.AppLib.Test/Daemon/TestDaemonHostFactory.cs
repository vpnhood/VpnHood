using VpnHood.AppUi.Hosting.Desktop.Abstractions;

namespace VpnHood.AppLib.Test.Daemon;

// A platform's app, which a connection never builds.
internal sealed class TestDaemonHostFactory : IAppDaemonHostFactory
{
    public IAppDaemonHost CreateService() => throw new NotSupportedException();
    public IAppDaemonHost CreateDev(string storagePath) => throw new NotSupportedException();
}
