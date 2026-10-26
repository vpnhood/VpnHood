using System.IO.Pipes;
using VpnHood.AppUi.Hosting.Desktop.Platform;

namespace VpnHood.AppLib.Test.Daemon;

// A session by the name it is given, which counts the launches that let the open window come forward.
internal sealed class TestDesktopSession(string name = "") : IDesktopSession
{
    private int _handForegroundCount;

    public string Name => name;
    public int HandForegroundCount => _handForegroundCount;

    public void HandForegroundTo(NamedPipeClientStream window) => Interlocked.Increment(ref _handForegroundCount);
}
