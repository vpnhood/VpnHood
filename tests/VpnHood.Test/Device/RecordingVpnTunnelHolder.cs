using VpnHood.Core.Client.VpnServices.Host;

namespace VpnHood.Test.Device;

// A tunnel holder that notes it was asked to let go, and takes LetGoTime to do it, as an app's adapter
// takes a moment to go.
public class RecordingVpnTunnelHolder : IVpnTunnelHolder
{
    private readonly TaskCompletionSource _asked = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool _hasLetGo;

    public TimeSpan LetGoTime { get; init; }
    public Task Asked => _asked.Task;
    public bool HasLetGo => _hasLetGo;

    public void OnTunnelRevoked()
    {
        _asked.TrySetResult();
        Thread.Sleep(LetGoTime);
        _hasLetGo = true;
    }
}
