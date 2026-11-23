using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.VpnAdapters.Abstractions;

namespace VpnHood.Core.Client.VpnServices.Host.Sessions;

// The host's connects and stops: which connect is current, the client it made, and the stop asked for. Each
// check-and-change is one method under the session lock, so no connect, stop or end comes in between. Connects overlap:
// a replaced one ends beside the new one, and sees the newer session wherever it checks.
internal class VpnServiceConnectState(
    VpnServiceContext context,
    IVpnServiceHandler vpnServiceHandler,
    EventHandler clientStateChanged)
{
    private CancellationTokenSource _connectCts = new();
    private Task _connectEnded = Task.CompletedTask; // the last connect's end, its failure's undoing included
    private Task _connectsEnded = Task.CompletedTask; // every connect's end and replaced client's close so far
    private IVpnAdapter? _vpnAdapter; // the last one registered (SetVpnAdapter), which a revoke takes down at once

    // the client the host follows, or null while a connect makes one
    public VpnHoodClient? Client { get; private set; }

    public bool IsDisconnectRequested { get; private set; }

    // why the stop was asked for, or null for a person's own
    public Exception? DisconnectException { get; private set; }

    public bool IsDisposed { get; private set; }

    // completes when every connect so far, and every replaced client's close, has ended: a session's end waits for it
    public Task ConnectsEnded {
        get {
            lock (context.SessionLock)
                return _connectsEnded;
        }
    }

    // Whether the client ended for the service's own reconnect, an end the app does not see: the server moved, or the
    // network lacks the address's IP version, and this service may connect by itself with no stop asked for
    public bool IsReconnectEnd(VpnHoodClient client)
    {
        return client.LastException is ServerAddressChangedException or ServerIpVersionUnsupportedException &&
               CanReconnectBySelf();
    }

    // the same, judged by the state that would be written
    public bool IsReconnectEnd(ConnectionInfo connectionInfo)
    {
        return connectionInfo.Error is { } error &&
               (error.Is<ServerAddressChangedException>() || error.Is<ServerIpVersionUnsupportedException>()) &&
               CanReconnectBySelf();
    }

    private bool CanReconnectBySelf()
    {
        return vpnServiceHandler.CanReconnect && !IsDisconnectRequested && !IsDisposed;
    }

    // Begins a connect, or says why none begins. endedSessionId: set for the service's own reconnect, else null
    public ConnectAdmission Admit(bool forceReconnect, int? endedSessionId)
    {
        lock (context.SessionLock) {
            // the host is gone, or a newer connect has replaced the session the reconnect was for
            if (IsDisposed || (endedSessionId is not null && context.SessionId != endedSessionId))
                return new ConnectAdmission();

            // a connection under way, or still starting, stays unless this connect is forced. Not checked for a
            // reconnect: the connect of the session it follows may still be finishing its last step
            if (!forceReconnect && endedSessionId is null &&
                (!_connectEnded.IsCompleted ||
                 Client is { State: not (ClientState.Disposed or ClientState.Disconnecting) }))
                return new ConnectAdmission { IsUnderWay = true, ClientUnderWay = Client };

            // a connect asked for undoes an earlier stop; the service's own reconnect gives way to it
            if (endedSessionId is null) {
                IsDisconnectRequested = false;
                DisconnectException = null;
            }

            if (IsDisconnectRequested)
                return new ConnectAdmission { StopSessionId = context.SessionId };

            // the old client is detached before the new session begins, so reading the session and then the client
            // never pairs an old client with a new session
            var oldClient = Client;
            Client = null;
            var connectCts = new CancellationTokenSource();
            var oldClientClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (oldClient is null)
                oldClientClosed.TrySetResult();

            var connect = new BegunConnect {
                ConnectToken = connectCts.Token,
                SessionId = context.BeginSession(),
                Ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
                PredecessorsEnded = AddEnd(_connectsEnded, oldClientClosed.Task),
                OldConnectCts = _connectCts,
                OldClient = oldClient,
                OldClientClosed = oldClientClosed
            };

            _connectCts = connectCts;
            _connectEnded = connect.Ended.Task;
            _connectsEnded = AddEnd(connect.PredecessorsEnded, connect.Ended.Task);
            return new ConnectAdmission { Connect = connect };
        }
    }

    // Takes an ended client for the service's own reconnect, once per client. Returns its session, or null if the
    // client is no longer the host's or a stop is asked for
    public int? ClaimEndedClient(VpnHoodClient client)
    {
        lock (context.SessionLock) {
            if (Client != client || IsDisconnectRequested || IsDisposed)
                return null;

            Client = null;
            return context.SessionId;
        }
    }

    // Publishes a connect's client only while that connect is current. The stop mark is checked as well as the token:
    // a stop may not have canceled the connect yet (a revoke takes the adapter down first). The listener is added
    // under the lock, so a stop that follows finds the client and the host hears of its end
    public bool TryPublishClient(VpnHoodClient client, int sessionId, CancellationToken cancellationToken)
    {
        lock (context.SessionLock) {
            if (IsDisposed || IsDisconnectRequested || context.SessionId != sessionId ||
                cancellationToken.IsCancellationRequested)
                return false;

            Client = client;
            client.StateChanged += clientStateChanged;
            return true;
        }
    }

    // Registers the adapter a revoke takes down at once, for the current connect only: one a newer connect took over
    // would hide the newer one's adapter
    public void SetVpnAdapter(IVpnAdapter vpnAdapter, int sessionId)
    {
        lock (context.SessionLock) {
            if (context.SessionId == sessionId)
                _vpnAdapter = vpnAdapter;
        }
    }

    // Marks a stop and, in the same step, takes what it stops: the connect to cancel, its session and client, and the
    // last registered adapter. A newer connect clears the mark under the same lock, so an older stop's reason never
    // lands on it. A stop with no reason keeps an earlier one, a revoke's say
    public RequestedDisconnect MarkDisconnectRequested(Exception? exception)
    {
        lock (context.SessionLock) {
            IsDisconnectRequested = true;
            exception ??= DisconnectException;
            DisconnectException = exception;
            return new RequestedDisconnect {
                ConnectCts = _connectCts,
                ConnectsEnded = _connectsEnded,
                SessionId = context.SessionId,
                Client = Client,
                VpnAdapter = _vpnAdapter,
                Exception = exception
            };
        }
    }

    // Whether a stop is asked for the session, with its reason: null for a person's own
    public bool TryGetStopReason(int sessionId, out Exception? reason)
    {
        lock (context.SessionLock) {
            reason = DisconnectException;
            return IsDisconnectRequested && context.SessionId == sessionId;
        }
    }

    // whether the session is still current and the host is not disposed
    public bool IsCurrent(int sessionId)
    {
        lock (context.SessionLock)
            return !IsDisposed && context.SessionId == sessionId;
    }

    // the ends so far and one more: the one alone where those so far have all come
    private static Task AddEnd(Task ends, Task end)
    {
        return ends.IsCompleted ? end : Task.WhenAll(ends, end);
    }

    // Marks the host disposed, once: after it no connect begins and no client is published. False if already marked
    public bool TryMarkDisposed()
    {
        lock (context.SessionLock) {
            if (IsDisposed)
                return false;

            IsDisposed = true;
            return true;
        }
    }

    // cancels the last connect; called after the mark, when no newer connect can replace the source read here
    public void CancelLastConnect()
    {
        _connectCts.TryCancel();
        _connectCts.Dispose();
    }
}
