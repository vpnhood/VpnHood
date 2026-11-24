using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Client;

// The order of an ad's tries is this class's alone (AdRoute): a connect's ad before the adapter, then around the
// tunnel, then through it where the app can show that kind of ad over the VPN (ClientOptions); an ad during a session,
// a rewarded one, skips the first. A failed try moves the wait to its next route at once, under the lock, so the app,
// answered after the move, shows the next try; a stop (AdFailure) ends the ad, and a connect with it. One wait at a
// time, which ends in one place (EndWait), under the lock that starts the next: what ends it also takes the session's
// traffic back into the tunnel and resets the connections made around it before the wait's callers go on, so a new
// wait never starts while the last one's end is still undoing its passthrough
internal class SessionAdHandler(ClientSession session) : ISessionAdHandler
{
    private readonly Lock _waitForAdLock = new();
    private AdWait? _activeWait;
    private bool _disposed;

    public event EventHandler? IsWaitingForChanged;
    public bool IsWaitingForAd => Volatile.Read(ref _activeWait) != null;
    public AdRoute? Route => Volatile.Read(ref _activeWait)?.Route;
    public bool IsLastRoute => Volatile.Read(ref _activeWait)?.IsLastRoute ?? false;

    // a connect's ad before the adapter: true where it showed, false where the routes after the adapter try it again. A
    // stop ends the connect here
    public async Task<bool> TryShowBeforeVpn(CancellationToken cancellationToken)
    {
        var failure = await TryWait([AdRoute.BeforeVpn], isFollowed: true, cancellationToken).Vhc();
        if (failure is { IsStop: true })
            ExceptionDispatchInfo.Throw(failure.Error);

        return failure == null;
    }

    // a connect's ad on the routes after the adapter: a failure ends the connect, unless the ad is flexible and the
    // failure no stop
    public async Task ShowAfterVpn(CancellationToken cancellationToken)
    {
        var adRequirement = session.Config.AdRequirement;
        var failure = await TryWait(GetRoutesAfterVpn(adRequirement), isFollowed: false, cancellationToken).Vhc();
        if (failure != null && (failure.IsStop || adRequirement is not AdRequirement.Flexible))
            ExceptionDispatchInfo.Throw(failure.Error);
    }

    // an ad during a session is a rewarded one, which extends it
    public async Task WaitForAd(CancellationToken cancellationToken)
    {
        var failure = await Wait(GetRoutesAfterVpn(AdRequirement.Rewarded), isFollowed: false, cancellationToken)
            .Vhc();

        if (failure != null)
            ExceptionDispatchInfo.Throw(failure.Error);
    }

    // through the tunnel last, where the app has a network for that kind of ad that can show over the VPN
    private IReadOnlyList<AdRoute> GetRoutesAfterVpn(AdRequirement adRequirement)
    {
        var canShowOverVpn = adRequirement == AdRequirement.Rewarded
            ? session.Config.CanShowRewardedAdOverVpn
            : session.Config.CanShowFlexibleAdOverVpn;

        return canShowOverVpn ? [AdRoute.Passthrough, AdRoute.OverVpn] : [AdRoute.Passthrough];
    }

    // The caller's own cancellation is no failed ad to try again; another, as the session's close, reads as a failure,
    // which the caller's look at the close explains
    private async Task<AdFailure?> TryWait(IReadOnlyList<AdRoute> routes, bool isFollowed,
        CancellationToken cancellationToken)
    {
        try {
            return await Wait(routes, isFollowed, cancellationToken).Vhc();
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested) {
            return new AdFailure(ex, IsStop: false);
        }
    }

    // the wait's failure, or null where the ad showed
    private Task<AdFailure?> Wait(IReadOnlyList<AdRoute> routes, bool isFollowed, CancellationToken cancellationToken)
    {
        // a canceled caller starts nothing: its start turned the passthrough on, and its end reset the connections
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<AdFailure?>(cancellationToken);

        AdWait wait;
        lock (_waitForAdLock) {
            if (_disposed)
                return Task.FromCanceled<AdFailure?>(new CancellationToken(canceled: true));

            // a caller that joins the wait under way: its own token ends its own wait alone
            if (_activeWait != null)
                return _activeWait.Completion.Task.WaitAsync(cancellationToken);

            wait = new AdWait(routes, isFollowed);
            Volatile.Write(ref _activeWait, wait);
            EnterRoute(wait.Route);
        }

        return WaitInternal(wait, cancellationToken);
    }

    // the token of the caller that started the wait ends it for every caller that joined it
    private async Task<AdFailure?> WaitInternal(AdWait wait, CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(() =>
            EndWait(wait, x => x.TrySetCanceled(cancellationToken)));

        Notify(wait);
        var failure = await wait.Completion.Task.Vhc();
        if (failure != null)
            VhLogger.Instance.LogError(failure.Error, "Failed to wait for ad. IsStop: {IsStop}", failure.IsStop);

        return failure;
    }

    // a notice that fails ends the wait or the route it announced, which had no one to show it
    private void Notify(AdWait wait)
    {
        try {
            IsWaitingForChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) {
            EndWait(wait, x => x.TrySetResult(new AdFailure(ex, IsStop: false)));
        }
    }

    // a try around the tunnel takes the session's new traffic out of it
    private void EnterRoute(AdRoute route)
    {
        if (route == AdRoute.Passthrough)
            session.PassthroughState.PassthroughForAd = true;
    }

    // and its end takes it back, resetting the connections made around the tunnel before anything goes on
    private void LeaveRoute(AdRoute route)
    {
        if (route != AdRoute.Passthrough)
            return;

        session.PassthroughState.PassthroughForAd = false;
        session.DropCurrentConnections();
    }

    // Ends the wait, unless it has ended already, with the session's traffic taken back into the tunnel first
    private void EndWait(AdWait wait, Action<TaskCompletionSource<AdFailure?>> complete)
    {
        lock (_waitForAdLock) {
            if (_activeWait != wait)
                return;

            Volatile.Write(ref _activeWait, null);
            LeaveRoute(wait.Route);
            complete(wait.Completion);
        }

        IsWaitingForChanged?.Invoke(this, EventArgs.Empty);
    }

    private void EndActiveWait(Action<TaskCompletionSource<AdFailure?>> complete)
    {
        var wait = Volatile.Read(ref _activeWait);
        if (wait != null)
            EndWait(wait, complete);
    }

    public void SetAdOk()
    {
        EndActiveWait(x => x.TrySetResult(null));
    }

    public void SetAdFailed(Exception ex, bool tryNextRoute)
    {
        ArgumentNullException.ThrowIfNull(ex);

        AdWait? wait;
        AdRoute failedRoute;
        bool isMoved;
        lock (_waitForAdLock) {
            wait = _activeWait;
            if (wait == null)
                return;

            failedRoute = wait.Route;
            isMoved = tryNextRoute && wait.HasNextRoute;
            if (isMoved) {
                LeaveRoute(failedRoute);
                wait.MoveNext();
                EnterRoute(wait.Route);
            }
        }

        // a failure no route helps is a stop, for the routes after the adapter too
        if (!isMoved) {
            EndWait(wait, x => x.TrySetResult(new AdFailure(ex, IsStop: !tryNextRoute)));
            return;
        }

        VhLogger.Instance.LogWarning(ex, "The ad failed on its route, so the next is tried. Route: {Route}, Next: {Next}",
            failedRoute, wait.Route);

        Notify(wait);
    }

    public async Task SendRewardedAdData(string rewardedAdData, CancellationToken cancellationToken)
    {
        // the wait of the moment: an answer that comes after it has ended does not end the next one
        var wait = Volatile.Read(ref _activeWait);
        try {
            // request reward from server
            var requestResult = await session.SendRequest<SessionResponse>(
                new RewardedAdRequest {
                    RequestId = UniqueIdFactory.Create(),
                    SessionId = session.Config.SessionId,
                    SessionKey = session.Config.SessionKey,
                    AdData = rewardedAdData
                },
                cancellationToken).Vhc();

            _ = requestResult.TryDisposeAsync();
            if (wait != null)
                EndWait(wait, x => x.TrySetResult(null));
        }
        catch (Exception ex) {
            // the server refused an ad that showed: a stop, as no other ad is asked of a person who watched one
            if (wait != null)
                EndWait(wait, x => x.TrySetResult(new AdFailure(ex, IsStop: true)));

            throw;
        }
    }

    public void Dispose()
    {
        lock (_waitForAdLock)
            _disposed = true;

        EndActiveWait(x => x.TrySetCanceled());
    }
}
