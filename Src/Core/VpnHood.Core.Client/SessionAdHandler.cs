using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Client;

internal class SessionAdHandler(ClientSession session) : ISessionAdHandler
{
    private TaskCompletionSource _waitForAdCts = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async Task WaitForAd(CancellationToken cancellationToken)
    {
        var oldState = session.State;
        var waitForAdCts = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var previousWaitForAdCts = Interlocked.Exchange(ref _waitForAdCts, waitForAdCts);

        try {
            session.PassthroughForAd = true;
            session.State = ClientState.WaitingForAd;
            previousWaitForAdCts.TrySetCanceled();
            await waitForAdCts.Task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            waitForAdCts.TrySetCanceled(cancellationToken);
            throw;
        }
        finally {
            session.PassthroughForAd = false;
            // reset connection after recovering passthrough mode to use VPN again
            session.DropCurrentConnections();
            session.State = oldState;
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
}