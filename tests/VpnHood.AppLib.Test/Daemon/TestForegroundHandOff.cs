using System.IO.Pipes;
using VpnHood.AppUi.Hosting.Desktop.Platform;

namespace VpnHood.AppLib.Test.Daemon;

// Counts the launches that let the open window come forward.
internal sealed class TestForegroundHandOff : IForegroundHandOff
{
    private int _handOffCount;

    public int HandOffCount => _handOffCount;

    public void HandTo(NamedPipeClientStream window) => Interlocked.Increment(ref _handOffCount);
}
