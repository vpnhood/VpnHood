using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Messaging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Client;

// One wait at a time, which ends in one place (EndWait), under the lock that starts the next: what ends it, an ad's
// outcome, its owner's cancellation or the session's end, also takes the session's traffic back into the tunnel and
// resets the connections made outside it before the wait's callers go on, so a new wait never starts while the last
// one's end is still undoing its passthrough, and no connection of a caller gone on is reset. A wait for an ad that can
// show over the VPN (ClientOptions.CanShowAdOverVpn) leaves the traffic's routing as it is, and its end resets nothing
internal class SessionAdHandler(ClientSession session) : ISessionAdHandler
{
    private readonly Lock _waitForAdLock = new();
    private TaskCompletionSource? _activeWait;
    private bool _disposed;

    public event EventHandler? IsWaitingForChanged;
    public bool IsWaitingForAd => Volatile.Read(ref _activeWait) != null;

    public async Task<Exception?> TryWaitForAd(CancellationToken cancellationToken)
    {
        try {
            await WaitForAd(cancellationToken).Vhc();
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // the caller's own cancellation is no failed ad to try again
            throw;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Failed to wait for ad.");
            return ex;
        }
    }

    public Task WaitForAd(CancellationToken cancellationToken)
    {
        // a canceled caller starts nothing: its start turned the passthrough on, and its end reset the connections
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled(cancellationToken);

        TaskCompletionSource wait;
        lock (_waitForAdLock) {
            if (_disposed)
                return Task.FromCanceled(new CancellationToken(canceled: true));

            // a caller that joins the wait under way: its own token ends its own wait alone
            if (_activeWait != null)
                return _activeWait.Task.WaitAsync(cancellationToken);

            wait = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _activeWait = wait;

            // the ad loads around the tunnel, unless it can show over the VPN (ClientOptions.CanShowAdOverVpn)
            if (!session.Config.CanShowAdOverVpn)
                session.PassthroughState.PassthroughForAd = true;
        }

        return WaitForAdInternal(wait, cancellationToken);
    }

    // the token of the caller that started the wait ends it for every caller that joined it; a notice that fails ends
    // the wait it announced, which had no owner to end it then
    private async Task WaitForAdInternal(TaskCompletionSource wait, CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(() =>
            EndWait(wait, x => x.TrySetCanceled(cancellationToken)));

        try {
            IsWaitingForChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) {
            EndWait(wait, x => x.TrySetException(ex));
        }

        await wait.Task.Vhc();
    }

    // Ends the wait, unless it has ended already, with the session's traffic taken back into the tunnel first
    private void EndWait(TaskCompletionSource wait, Action<TaskCompletionSource> complete)
    {
        lock (_waitForAdLock) {
            if (_activeWait != wait)
                return;

            _activeWait = null;

            // reset connection after recovering passthrough mode to use VPN again, before the callers go on; a wait
            // over the VPN sent nothing around the tunnel, so its connections stay
            if (!session.Config.CanShowAdOverVpn) {
                session.PassthroughState.PassthroughForAd = false;
                session.DropCurrentConnections();
            }

            complete(wait);
        }

        IsWaitingForChanged?.Invoke(this, EventArgs.Empty);
    }

    private void EndActiveWait(Action<TaskCompletionSource> complete)
    {
        var wait = Volatile.Read(ref _activeWait);
        if (wait != null)
            EndWait(wait, complete);
    }

    public void SetAdOk()
    {
        EndActiveWait(x => x.TrySetResult());
    }

    public void SetAdFailed(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        EndActiveWait(x => x.TrySetException(ex));
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
                EndWait(wait, x => x.TrySetResult());
        }
        catch (Exception ex) {
            if (wait != null)
                EndWait(wait, x => x.TrySetException(ex));
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
