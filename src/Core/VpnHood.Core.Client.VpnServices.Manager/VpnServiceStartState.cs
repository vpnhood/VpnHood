using VpnHood.Core.Client.Abstractions;

namespace VpnHood.Core.Client.VpnServices.Manager;

// Which start owns the VPN service's state while it initializes: the latest. Under the lock, an older start's end
// comes either before a newer start's Initializing, or after it and leaves the state alone
internal sealed class VpnServiceStartState(VpnServiceStatus status)
{
    private readonly Lock _lock = new();
    private Scope? _current;

    public bool IsInitializing {
        get {
            lock (_lock)
                return _current != null;
        }
    }

    // a start cancelled while its stop ran takes nothing over
    public Scope Begin(CancellationToken cancellationToken)
    {
        lock (_lock) {
            cancellationToken.ThrowIfCancellationRequested();
            var scope = new Scope(this);
            status.Set(ClientState.Initializing);
            _current = scope;
            return scope;
        }
    }

    private void End(Scope scope, Exception? error)
    {
        lock (_lock) {
            if (!ReferenceEquals(_current, scope))
                return; // a newer start owns the state

            // It looks like the service is not running, set the state to disposed if it is still in
            // initializing state
            if (error != null && status.TryRead()?.ClientState == ClientState.Initializing)
                status.Set(ClientState.Disposed, error);

            _current = null;
        }
    }

    // one start's hold on the state: it keeps the start's failure for its end
    public sealed class Scope(VpnServiceStartState owner) : IDisposable
    {
        private Exception? _error;

        public void Fail(Exception error) => _error = error;
        public void Dispose() => owner.End(this, _error);
    }
}
