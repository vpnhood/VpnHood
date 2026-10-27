using System.Collections.Concurrent;
using VpnHood.AppUi.Hosting.Desktop.Abstractions;

namespace VpnHood.AppLib.Test.Daemon;

// A UI that runs nothing and counts how often a second launch asked for its window and to connect, and
// keeps the access keys it was handed.
internal sealed class TestDesktopUi : IDesktopUi
{
    private int _bringToFrontCount;
    private int _quickLaunchCount;
    private readonly ConcurrentQueue<string> _importedAccessKeys = new();

    public int BringToFrontCount => _bringToFrontCount;
    public int QuickLaunchCount => _quickLaunchCount;
    public IReadOnlyList<string> ImportedAccessKeys => _importedAccessKeys.ToArray();

    public void Run(DesktopUiParams uiParams, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public void RunMessage(DesktopUiMessageParams messageParams, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task BringToFront(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _bringToFrontCount);
        return Task.CompletedTask;
    }

    public Task ImportAccessKey(string accessKey, CancellationToken cancellationToken)
    {
        _importedAccessKeys.Enqueue(accessKey);
        return Task.CompletedTask;
    }

    public Task QuickLaunch(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _quickLaunchCount);
        return Task.CompletedTask;
    }
}
