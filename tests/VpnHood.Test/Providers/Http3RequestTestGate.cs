namespace VpnHood.Test.Providers;

internal sealed class Http3RequestTestGate
{
    private readonly TaskCompletionSource _applicationStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _inputConsumed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _responseSubmitted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task WaitForApplication(CancellationToken cancellationToken) =>
        _applicationStarted.Task.WaitAsync(cancellationToken);

    public Task WaitForInput(CancellationToken cancellationToken) => _inputConsumed.Task.WaitAsync(cancellationToken);

    public Task WaitForResponse(CancellationToken cancellationToken) => _responseSubmitted.Task.WaitAsync(cancellationToken);

    public void ApplicationStarted() => _applicationStarted.TrySetResult();
    public void InputConsumed() => _inputConsumed.TrySetResult();
    public void ResponseSubmitted() => _responseSubmitted.TrySetResult();
}
