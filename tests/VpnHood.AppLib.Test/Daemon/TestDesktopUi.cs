using VpnHood.AppUi.Hosting.Desktop.Abstractions;

namespace VpnHood.AppLib.Test.Daemon;

// A UI that runs nothing and counts how often a second launch asked for its window.
internal sealed class TestDesktopUi : IDesktopUi
{
    private int _bringToFrontCount;

    public int BringToFrontCount => _bringToFrontCount;

    public void Run(DesktopUiParams uiParams, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public void RunMessage(DesktopUiMessageParams messageParams, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task BringToFront(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _bringToFrontCount);
        return Task.CompletedTask;
    }
}
