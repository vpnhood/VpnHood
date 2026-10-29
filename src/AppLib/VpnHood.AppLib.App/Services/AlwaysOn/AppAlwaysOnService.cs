using System.Net.Sockets;
using System.Security.Authentication;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Api.Exceptions;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Net.Toolkit.ApiClients;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.AppLib.App.Services.AlwaysOn;

// Always on where the app keeps it itself (AlwaysOnSupport.App: a desktop's service, which starts at
// boot). It connects at the app's start, before anyone signs in, and sorts why a session ended, whichever
// connect began it: a failure to reach a working server, or a VPN adapter that stopped on its own, is tried
// again after a pause that grows, and anything else stops it - a refused key, a used-up quota, a plan that
// does not allow it, an error nobody thought of - and so does a person's disconnect. A stop stands until a
// person connects or the service restarts, and one by an error also until the plan changes. A revoke,
// another app taking the tunnel, stands across a restart too (AppPersistState.IsVpnServiceRevoked), or two
// apps with always on would take the tunnel from each other in turn. Its own connect gives way to anything
// that came after it chose to connect, so it never undoes a person's connect or disconnect.
internal sealed class AppAlwaysOnService : IDisposable
{
    private readonly VpnHoodApp _app;
    private readonly AppAlwaysOnOptions _options;
    private readonly AppPersistState _persistState;
    private readonly Lock _lock = new();
    private CancellationTokenSource _retryCts = new();
    private int _connectNumber; // the latest connect's, whichever began it
    private bool _isConnecting; // the latest connect has not ended
    private Plan? _connectPlan; // the plan the latest connect tries
    private DateTime? _sessionTime; // when the latest connect's session came up, until its end is sorted
    private int _retryCount;
    private AlwaysOnStatus _status = AlwaysOnStatus.On;
    private DateTime? _retryTime;
    private ApiError? _error;
    private Plan? _stoppedPlan;
    private bool _disposed;

    public AppAlwaysOnService(VpnHoodApp app, AppAlwaysOnOptions options, AppPersistState persistState)
    {
        _app = app;
        _options = options;
        _persistState = persistState;
        app.VpnProfileService.Changed += VpnProfileService_Changed;
    }

    private bool IsSupported => _app.Features.AlwaysOnSupport == AlwaysOnSupport.App;
    private bool IsOn => IsSupported && _app.UserSettings.AlwaysOn;

    public AlwaysOnState? State {
        get {
            if (!IsSupported)
                return null;

            lock (_lock) {
                var isOn = IsOn;
                return new AlwaysOnState {
                    Status = isOn ? _status : AlwaysOnStatus.Off,
                    RetryTime = isOn ? _retryTime : null,
                    Error = isOn ? _error : null
                };
            }
        }
    }

    // the app's start: connect, unless a revoke stands
    public void Start()
    {
        lock (_lock) {
            if (!IsOn || _disposed)
                return;

            // the settings, applied as the app was built, may have stopped it for the revoke already
            if (_persistState.IsVpnServiceRevoked) {
                if (_status != AlwaysOnStatus.Stopped)
                    Stop(new VpnServiceRevokedException().ToApiError(), plan: null);
                return;
            }
        }

        VhLogger.Instance.LogInformation("Always on connects at the start.");
        _ = Connect(CancellationToken.None);
    }

    // A connect begins, a person's or always on's own, with the profile it was given or the current one; the
    // number is EndConnect's. How an older one ends is no longer sorted, and a person's ends whatever stopped
    // always on, the revoke too. Always on's own gives way (null) to what came after it chose to connect - a
    // person's disconnect, the switch turned off - and takes over no connect and no session.
    public int? BeginConnect(bool isAlwaysOn, Guid? vpnProfileId)
    {
        lock (_lock) {
            if (isAlwaysOn && (!IsOn || _disposed || _status == AlwaysOnStatus.Stopped || _isConnecting ||
                               _sessionTime != null)) {
                VhLogger.Instance.LogInformation("Always on's connect gives way to what came after it.");
                return null;
            }

            _connectNumber++;
            _isConnecting = true;
            _connectPlan = IsSupported ? GetPlan(vpnProfileId) : null;
            _sessionTime = null;
            CancelRetry();
            _status = AlwaysOnStatus.On;
            _retryTime = null;
            _error = null;
            if (!isAlwaysOn) {
                _stoppedPlan = null;
                _retryCount = 0;
                if (IsSupported) {
                    _persistState.IsVpnServiceRevoked = false;
                    _persistState.VpnServiceRevokeClearedTime = DateTime.UtcNow; // the service's stamps lag behind it
                }
            }

            return _connectNumber;
        }
    }

    // How the connect BeginConnect numbered ended: its session is up, or it failed. The app's own cancel is
    // the canceller's to sort: a person's disconnect stops always on itself, and a newer connect ends its own
    // way; a caller that gave up on its connect is sorted as a failure.
    public void EndConnect(int connectNumber, Exception? exception)
    {
        lock (_lock) {
            if (connectNumber != _connectNumber)
                return;

            _isConnecting = false;
            if (exception is null) {
                _sessionTime = FastDateTime.UtcNow;
                return;
            }

            if (exception is not UserCanceledException)
                Sort(exception.ToApiError(), sessionWasUp: false);
        }
    }

    // The latest connect's session ended outside a connect and a disconnect: by itself (the server's end,
    // the network's, a revoke), or by the app over an ad it could not show. One that held starts the pauses
    // over, and one that gave no reason stops always on as an error does, not as a person's disconnect.
    public void SessionEnded(ApiError? error)
    {
        lock (_lock) {
            if (_sessionTime is not { } sessionTime)
                return;

            _sessionTime = null;
            if (FastDateTime.UtcNow - sessionTime >= _options.RetryPausesResetTime)
                _retryCount = 0;

            error ??= new VpnServiceException("The VPN service ended the session without a reason.").ToApiError();
            Sort(error, sessionWasUp: true);
        }
    }

    // The app's own disconnect - a person's, a dispose's, a failed ad's - has ended a session, with the VPN
    // service's last word: another app may have revoked it just before, which the app had not seen yet, and the
    // revoke stands all the same. A word older than a person's last connect is not that: the connect ended its
    // revoke, and one that never started the service left the word behind.
    public void DisconnectEnded(ConnectionInfo lastWord)
    {
        lock (_lock) {
            if (lastWord.CreatedTime < _persistState.VpnServiceRevokeClearedTime)
                return;

            NoteRevoke(lastWord.Error);
        }
    }

    // a person's disconnect, which stops always on until a person connects or the service restarts
    public void PersonDisconnecting()
    {
        lock (_lock) {
            _connectNumber++;
            _isConnecting = false;
            _sessionTime = null;
            if (IsOn)
                Stop(error: null, plan: null);
        }
    }

    // The switch, and which profile is chosen, which may be another plan. Switched off, nothing waits, and a
    // session up stays so; switched on, nothing connects until the next failure or the next start.
    public void ApplySettings()
    {
        lock (_lock) {
            if (!IsSupported || _disposed)
                return;

            if (!_app.UserSettings.AlwaysOn) {
                CancelRetry();
                _status = AlwaysOnStatus.On;
                _retryTime = null;
                _error = null;
                _stoppedPlan = null;
                _retryCount = 0;
                return;
            }

            if (_persistState.IsVpnServiceRevoked && _status != AlwaysOnStatus.Stopped)
                Stop(new VpnServiceRevokedException().ToApiError(), plan: null);
        }

        TryAfterPlanChange();
    }

    private void VpnProfileService_Changed(object? sender, EventArgs e)
    {
        TryAfterPlanChange();
    }

    private void TryAfterPlanChange()
    {
        lock (_lock) {
            if (!ResumeAfterPlanChange())
                return;
        }

        // off the caller's thread: a settings save may be under way on it
        _ = Task.Run(() => Connect(CancellationToken.None));
    }

    // Under the lock. A stop by an error tries once more under another plan: a key or a code bought, typed or
    // given, or another profile, whether it came after the stop or during the try that stopped.
    private bool ResumeAfterPlanChange()
    {
        if (_disposed || !IsOn || _status != AlwaysOnStatus.Stopped ||
            _stoppedPlan is not { } stoppedPlan || GetPlan() == stoppedPlan)
            return false;

        _status = AlwaysOnStatus.On;
        _error = null;
        _stoppedPlan = null;
        VhLogger.Instance.LogInformation("The plan has changed, so always on tries once more.");
        return true;
    }

    // Under the lock. With always on on, a failure to reach a working server waits its pause, and anything else
    // stops it, under the plan the try had.
    private void Sort(ApiError error, bool sessionWasUp)
    {
        var isRevoke = NoteRevoke(error);
        if (_disposed || !IsOn)
            return;

        if (!IsTriedAgain(error, sessionWasUp)) {
            Stop(error, plan: isRevoke ? null : _connectPlan);
            if (ResumeAfterPlanChange())
                _ = Task.Run(() => Connect(CancellationToken.None));
            return;
        }

        var pauses = _options.RetryPauses;
        var pause = pauses[Math.Min(_retryCount, pauses.Count - 1)] * (1 - Random.Shared.NextDouble() / 5);
        _retryCount++;
        _status = AlwaysOnStatus.Waiting;
        _retryTime = FastDateTime.UtcNow + pause;
        _error = error;
        VhLogger.Instance.LogInformation("Always on tries again in {Pause}. Error: {Error}", pause, error.Message);
        _ = RetryAfter(pause, _retryCts.Token);
    }

    // Under the lock. Another app took the tunnel: kept across restarts whether always on is on or not, or two
    // apps with always on would take the tunnel from each other in turn.
    private bool NoteRevoke(ApiError? error)
    {
        if (!IsSupported || error?.Is<VpnServiceRevokedException>() != true)
            return false;

        _persistState.IsVpnServiceRevoked = true;
        return true;
    }

    // Under the lock. A null error is a person's disconnect.
    private void Stop(ApiError? error, Plan? plan)
    {
        CancelRetry();
        _status = AlwaysOnStatus.Stopped;
        _retryTime = null;
        _error = error;
        _stoppedPlan = plan;
        if (IsOn)
            VhLogger.Instance.LogInformation("Always on has stopped. Error: {Error}", error?.Message ?? "none");
    }

    // A failure to reach a working server: no network yet, as at boot; a server, its name or a proxy out of
    // reach; a timeout, a broken connection or handshake, or a connect its caller gave up on; the app's own VPN
    // service out of reach for a while; a session the server or the network ended, or one the server lost,
    // whose access error after the session was up is no refused key (a key it refuses fails the next connect,
    // and an HTTP 401, UnauthorizedAccessException, is one); a server busy or down for maintenance. And the
    // VPN adapter that stopped on its own - its interface disabled or gone, its I/O broken - whose next connect
    // makes a new one. And a connect refused while the app installs its update, whose new version's start
    // connects; one whose install ended without installing is tried again.
    internal static bool IsTriedAgain(ApiError error, bool sessionWasUp)
    {
        if (error.Is<SessionException>() || error.Is<MaintenanceException>())
            return error.Data.GetValueOrDefault(nameof(SessionResponse.ErrorCode)) switch {
                nameof(SessionErrorCode.SessionClosed) or nameof(SessionErrorCode.SessionExpired) or
                    nameof(SessionErrorCode.NoServerAvailable) or nameof(SessionErrorCode.Maintenance) => true,
                nameof(SessionErrorCode.AccessError) => sessionWasUp,
                _ => false
            };

        return error.Is<NoInternetException>() ||
               error.Is<UnreachableServerException>() ||
               error.Is<UnreachableServerLocationException>() ||
               error.Is<UnreachableProxyServerException>() ||
               error.Is<EndPointDiscoveryException>() ||
               error.Is<ConnectionTimeoutException>() ||
               error.Is<SocketException>() ||
               error.Is<TimeoutException>() ||
               error.Is<IOException>() ||
               error.Is<AuthenticationException>() ||
               error.Is<OperationCanceledException>() ||
               error.Is<TaskCanceledException>() ||
               error.Is<VpnServiceUnreachableException>() ||
               error.Is<VpnServiceNotReadyException>() ||
               error.Is<VpnServiceTimeoutException>() ||
               error.Is<VpnAdapterFailedException>() ||
               error.Is<AppUpdatingException>();
    }

    private Plan GetPlan(Guid? vpnProfileId = null)
    {
        var profileInfo = vpnProfileId is { } id ? _app.VpnProfileService.FindInfo(id) : _app.CurrentVpnProfileInfo;
        var accessCode = profileInfo != null ? _app.VpnProfileService.FindById(profileInfo.VpnProfileId)?.AccessCode : null;
        return new Plan(profileInfo?.VpnProfileId, profileInfo?.TokenId, accessCode, profileInfo?.IsPremium == true);
    }

    private async Task RetryAfter(TimeSpan pause, CancellationToken cancellationToken)
    {
        try {
            await Task.Delay(pause, cancellationToken).Vhc();
        }
        catch (OperationCanceledException) {
            return;
        }

        // a connect or a disconnect since cancelled it; BeginConnect makes it give way to one that comes after this
        if (cancellationToken.IsCancellationRequested)
            return;

        // a connect of its own, not under the retry's token, which its BeginConnect cancels
        await Connect(CancellationToken.None).Vhc();
    }

    // Always on's own connect, with the current profile and no window to ask anyone anything. EndConnect
    // has sorted how it ended, and the app has logged it.
    private async Task Connect(CancellationToken cancellationToken)
    {
        try {
            await _app.Connect(uiContext: null, new ConnectOptions { IsAlwaysOn = true }, cancellationToken).Vhc();
        }
        catch (Exception) {
            // sorted
        }
    }

    // under the lock
    private void CancelRetry()
    {
        _retryCts.TryCancel();
        _retryCts.Dispose();
        _retryCts = new CancellationTokenSource();
    }

    public void Dispose()
    {
        lock (_lock) {
            if (_disposed)
                return;

            _disposed = true;
            _app.VpnProfileService.Changed -= VpnProfileService_Changed;
            CancelRetry();
        }
    }

    // what a plan change is: another profile, key or code, or the profile's premium coming or going
    private sealed record Plan(Guid? VpnProfileId, string? TokenId, string? AccessCode, bool IsPremium);
}
