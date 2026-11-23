using VpnHood.Core.Client.VpnServices.Abstractions;

namespace VpnHood.Core.Client.VpnServices.Host.Sessions;

// The service's notification, one platform call at a time. A session's state is shown only while the session is
// current and has not asked for the notification's stop: a late one would put the notification back up, or show a
// newer connect the old session's state.
internal class VpnServiceNotification(IVpnServiceHandler vpnServiceHandler, VpnServiceContext context)
{
    private readonly Lock _lock = new();
    private int _stoppedSessionId; // the last session that asked for the stop, whether or not the platform's worked

    public void Show(ConnectionInfo connectionInfo, int sessionId, CancellationToken cancellationToken)
    {
        lock (_lock) {
            if (sessionId != context.SessionId || sessionId <= _stoppedSessionId)
                return;

            vpnServiceHandler.ShowNotification(connectionInfo, cancellationToken);
        }
    }

    // stops the notification for a session's end, unless a newer connect has begun, whose notification it now is
    public void Stop(int sessionId)
    {
        lock (_lock) {
            if (sessionId != context.SessionId)
                return;

            _stoppedSessionId = Math.Max(_stoppedSessionId, sessionId);
            vpnServiceHandler.StopNotification();
        }
    }
}
