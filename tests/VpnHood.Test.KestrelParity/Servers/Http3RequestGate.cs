namespace VpnHood.Test.KestrelParity.Servers;

internal sealed class Http3RequestGate
{
    private readonly TaskCompletionSource _applicationStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _prefixConsumed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void OnApplicationStarted() => _applicationStarted.TrySetResult();
    public void OnPrefixConsumed() => _prefixConsumed.TrySetResult();

    public Task WaitForApplicationStarted(CancellationToken cancellationToken) =>
        _applicationStarted.Task.WaitAsync(cancellationToken);

    public Task WaitForPrefixConsumed(CancellationToken cancellationToken) =>
        _prefixConsumed.Task.WaitAsync(cancellationToken);
}
