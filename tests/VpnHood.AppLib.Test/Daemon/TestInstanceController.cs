using VpnHood.AppUi.Hosting.Desktop.Abstractions;

namespace VpnHood.AppLib.Test.Daemon;

// The running instance, as far as a connection asks about it: whether it is running.
internal sealed class TestInstanceController : IAppInstanceController
{
    public bool Running { get; set; } = true;
    public string NotRunningHint => "The test service is not running. Start it with: vhtest service start";

    public Task<bool> IsRunning(CancellationToken cancellationToken) => Task.FromResult(Running);
    public Task<int> Start(CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<int> Stop(CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<int> Restart(CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<int> ShowStatus(CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<int> ShowOfflineLog(bool follow, int lines, CancellationToken cancellationToken) => throw new NotSupportedException();
}
