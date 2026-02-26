using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Client;

internal class SessionAdHandler(ClientSession session) : ISessionAdHandler
{
    private TaskCompletionSource _waitForAdCts = VhUtils.CreateCompletedTcs();

    public event EventHandler? IsWaitingForChanged;
    public bool IsWaitingForAd => !_waitForAdCts.Task.IsCompleted;

    private readonly AsyncLock _waitForAdLock = new();

    public async Task WaitForAd(CancellationToken cancellationToken)
    {
        using var scopeLock = await _waitForAdLock.LockAsync(cancellationToken).Vhc();

        try {
            _waitForAdCts = new TaskCompletionSource();
            session.PassthroughForAd = true;
            IsWaitingForChanged?.Invoke(this, EventArgs.Empty);
            await _waitForAdCts.Task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            _waitForAdCts.TrySetCanceled(cancellationToken);
            throw;
        }
        finally {
            session.PassthroughForAd = false;
            // reset connection after recovering passthrough mode to use VPN again
            session.DropCurrentConnections();
            IsWaitingForChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public void SetAdOk()
    {
        _waitForAdCts.TrySetResult();
    }

    public void SetAdFailed(Exception ex)
    {
        _waitForAdCts.TrySetException(ex);
    }

    public async Task SendRewardedAdData(string rewardedAdData, CancellationToken cancellationToken)
    {
        try {
            // request reward from server
            using var requestResult = await session.SendRequest<SessionResponse>(
                new RewardedAdRequest {
                    RequestId = UniqueIdFactory.Create(),
                    SessionId = session.SessionId,
                    SessionKey = session.SessionKey,
                    AdData = rewardedAdData
                },
                cancellationToken).Vhc();

            SetAdOk();
        }
        catch (Exception ex) {
            SetAdFailed(ex);
            throw;
        }
    }

    public void Dispose()
    {
        _waitForAdCts.TrySetCanceled();
    }
}