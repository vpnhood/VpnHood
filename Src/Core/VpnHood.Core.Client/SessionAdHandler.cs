using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Client;

internal class SessionAdHandler(ClientSession session) : ISessionAdHandler
{
    private TaskCompletionSource _waitForAdCts = VhUtils.CreateCompletedTcs();

    public event EventHandler? IsWaitingForChanged;
    public bool IsWaitingForAd => !_waitForAdCts.Task.IsCompleted;

    private readonly Lock _waitForAdLock = new();

    public async Task<Exception?> TryWaitForAd(CancellationToken cancellationToken)
    {
        try {
            await WaitForAd(cancellationToken);
            return null;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Failed to wait for ad.");
            return ex;
        }
    }

    public Task WaitForAd(CancellationToken cancellationToken)
    {
        lock (_waitForAdLock) {
            return _waitForAdCts.Task.IsCompleted 
                ? WaitForAdInternal(cancellationToken) 
                : _waitForAdCts.Task.WaitAsync(cancellationToken);
        }
    }

    private async Task WaitForAdInternal(CancellationToken cancellationToken)
    {
        try {
            _waitForAdCts = new TaskCompletionSource();
            session.PassthroughState.PassthroughForAd = true;
            IsWaitingForChanged?.Invoke(this, EventArgs.Empty);
            await _waitForAdCts.Task.WaitAsync(cancellationToken);
        }
        finally {
            session.PassthroughState.PassthroughForAd = false;
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
                    SessionId = session.Config.SessionId,
                    SessionKey = session.Config.SessionKey,
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