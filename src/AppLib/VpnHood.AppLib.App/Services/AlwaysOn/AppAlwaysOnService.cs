using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Api.Exceptions;
using VpnHood.Core.Client.Abstractions.Exceptions;
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
// connect began it: a failure to reach a working server is tried again after a pause that grows, and
// anything else stops it - a refused key, a used-up quota, a plan that does not allow it, an error nobody
// thought of - and so does a person's disconnect. A stop stands until a person connects or the service
// restarts, and one by an error also until the plan changes. A revoke, another app taking the tunnel,
// stands across a restart too (AppPersistState.IsVpnServiceRevoked), or two apps with always on would
// take the tunnel from each other in turn.
internal sealed class AppAlwaysOnService : IDisposable
{
    private readonly VpnHoodApp _app;
    private readonly AppAlwaysOnOptions _options;
    private readonly AppPersistState _persistState;
    private readonly Lock _lock = new();
    private CancellationTokenSource _retryCts = new();
    private int _connectNumber; // the latest connect's, whichever began it
    private DateTime? _sessionTime; // when the latest connect's session came up, until its end is sorted
    private int _retryCount;
    private AlwaysOnStatus _status = AlwaysOnStatus.On;
    private DateTime? _retryTime;
    private ApiError? _error;
    private (Guid? VpnProfileId, string? TokenId, string? AccessCode, bool IsPremium)? _stoppedPlan;
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

            if (_persistState.IsVpnServiceRevoked) {
                Stop(new VpnServiceRevokedException().ToApiError(), plan: null);
                return;
            }
        }

        VhLogger.Instance.LogInformation("Always on connects at the start.");
        _ = Connect();
    }

    // A connect begins, a person's or always on's own; the number is EndConnect's. How an older one ends is
    // no longer sorted, and a person's ends whatever stopped always on, the revoke too.
    public int BeginConnect(bool isAlwaysOn)
    {
        lock (_lock) {
            _connectNumber++;
            _sessionTime = null;
            CancelRetry();
            _status = AlwaysOnStatus.On;
            _retryTime = null;
            _error = null;
            if (!isAlwaysOn) {
                _stoppedPlan = null;
                _retryCount = 0;
                if (IsSupported)
                    _persistState.IsVpnServiceRevoked = false;
            }

            return _connectNumber;
        }
    }

    // How the connect BeginConnect numbered ended: its session is up, or it failed. A cancel is the
    // canceller's to sort: a person's disconnect stops always on itself, and a newer connect ends its own way.
    public void EndConnect(int connectNumber, Exception? exception)
    {
        lock (_lock) {
            if (connectNumber != _connectNumber)
                return;

            if (exception is null) {
                _sessionTime = FastDateTime.UtcNow;
                return;
            }

            if (exception is not UserCanceledException)
                Sort(exception.ToApiError());
        }
    }

    // The latest connect's session ended outside a connect and a disconnect: by itself (the server's end,
    // the network's, a revoke), or by the app over an ad it could not show. One that held starts the pauses
    // over.
    public void SessionEnded(ApiError? error)
    {
        lock (_lock) {
            if (_sessionTime is not { } sessionTime)
                return;

            _sessionTime = null;
            if (FastDateTime.UtcNow - sessionTime >= _options.RetryPausesResetTime)
                _retryCount = 0;

            Sort(error);
        }
    }

    // a person's disconnect, which stops always on until a person connects or the service restarts
    public void PersonDisconnecting()
    {
        lock (_lock) {
            _connectNumber++;
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

    // A stop by an error tries once more under another plan: a key or a code bought, typed or given.
    private void TryAfterPlanChange()
    {
        lock (_lock) {
            if (_disposed || !IsOn || _status != AlwaysOnStatus.Stopped ||
                _stoppedPlan is not { } stoppedPlan || GetPlan() == stoppedPlan)
                return;

            _status = AlwaysOnStatus.On;
            _error = null;
            _stoppedPlan = null;
        }

        if (!_app.IsIdle)
            return;

        // off the caller's thread: a settings save may be under way on it
        VhLogger.Instance.LogInformation("The plan has changed, so always on tries once more.");
        _ = Task.Run(Connect);
    }

    // Under the lock. A revoke is kept across restarts, whether always on is on or not; with it on, a failure
    // to reach a working server waits its pause, and anything else stops it.
    private void Sort(ApiError? error)
    {
        if (!IsSupported || _disposed)
            return;

        var isRevoke = error?.Is<VpnServiceRevokedException>() == true;
        if (isRevoke)
            _persistState.IsVpnServiceRevoked = true;

        if (!_app.UserSettings.AlwaysOn)
            return;

        if (error is null || !IsTriedAgain(error)) {
            Stop(error, plan: error is null || isRevoke ? null : GetPlan());
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

    // Under the lock. A null error is a person's disconnect, or an end that gave no reason.
    private void Stop(ApiError? error, (Guid?, string?, string?, bool)? plan)
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
    // reach; a timeout; a session the server or the network ended; a server busy or down for maintenance.
    private static bool IsTriedAgain(ApiError error)
    {
        if (error.Is<SessionException>() || error.Is<MaintenanceException>())
            return error.Data.GetValueOrDefault(nameof(SessionResponse.ErrorCode)) is
                nameof(SessionErrorCode.SessionClosed) or nameof(SessionErrorCode.SessionExpired) or
                nameof(SessionErrorCode.NoServerAvailable) or nameof(SessionErrorCode.Maintenance);

        return error.Is<NoInternetException>() ||
               error.Is<UnreachableServerException>() ||
               error.Is<UnreachableServerLocationException>() ||
               error.Is<UnreachableProxyServerException>() ||
               error.Is<EndPointDiscoveryException>() ||
               error.Is<ConnectionTimeoutException>() ||
               error.Is<SocketException>() ||
               error.Is<TimeoutException>();
    }

    // what a plan change is: another profile, key or code, or the profile's premium coming or going
    private (Guid?, string?, string?, bool) GetPlan()
    {
        var profileInfo = _app.CurrentVpnProfileInfo;
        var accessCode = profileInfo != null ? _app.VpnProfileService.FindById(profileInfo.VpnProfileId)?.AccessCode : null;
        return (profileInfo?.VpnProfileId, profileInfo?.TokenId, accessCode, profileInfo?.IsPremium == true);
    }

    private async Task RetryAfter(TimeSpan pause, CancellationToken cancellationToken)
    {
        try {
            await Task.Delay(pause, cancellationToken).Vhc();
        }
        catch (OperationCanceledException) {
            return;
        }

        // a session up or on its way is left alone; a person's connect would have cancelled this anyway
        if (cancellationToken.IsCancellationRequested || !_app.IsIdle)
            return;

        await Connect().Vhc();
    }

    // Always on's own connect, with the current profile and no window to ask anyone anything. EndConnect
    // has sorted how it ended, and the app has logged it.
    private async Task Connect()
    {
        try {
            await _app.Connect(uiContext: null, new ConnectOptions { IsAlwaysOn = true }, CancellationToken.None).Vhc();
        }
        catch (Exception) {
            // sorted
        }
    }

    // under the lock
    private void CancelRetry()
    {
        _retryCts.Cancel();
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
}
