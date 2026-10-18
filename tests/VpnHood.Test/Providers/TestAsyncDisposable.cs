namespace VpnHood.Test.Providers;

// An async disposable whose close can be held until released, and can fail
public class TestAsyncDisposable : IAsyncDisposable
{
    private readonly TaskCompletionSource _closeReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool HoldClose { get; init; }
    public Exception? CloseFailure { get; init; }

    public void ReleaseClose() => _closeReleased.TrySetResult();

    public async ValueTask DisposeAsync()
    {
        if (HoldClose)
            await _closeReleased.Task;

        if (CloseFailure != null)
            throw CloseFailure;
    }
}
