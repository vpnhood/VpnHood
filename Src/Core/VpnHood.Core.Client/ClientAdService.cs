using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Utils;
using VpnHood.Core.Tunneling.Messaging;

namespace VpnHood.Core.Client;

public class ClientAdService(VpnHoodClient client)
{
    public TaskCompletionSource<AdResult>? AdRequestTaskCompletionSource { get; private set; }
    public AdRequest? AdRequest { get; private set; }

    public Task<AdResult> ShowRewarded(string sessionId, CancellationToken cancellationToken)
    {
        var adRequest = new AdRequest {
            SessionId = sessionId,
            AdRequestType = AdRequestType.Rewarded,
            RequestId = Guid.NewGuid()
        };
        return Show(adRequest, cancellationToken);
    }

    public Task<AdResult> ShowInterstitial(string sessionId, CancellationToken cancellationToken)
    {
        var adRequest = new AdRequest {
            SessionId = sessionId,
            AdRequestType = AdRequestType.Interstitial, // Corrected AdRequestType
            RequestId = Guid.NewGuid()
        };
        return Show(adRequest, cancellationToken);
    }

    public async Task<AdResult?> TryShowInterstitial(string sessionId, CancellationToken cancellationToken)
    {
        try {
            return await ShowInterstitial(sessionId, cancellationToken);
        }
        // ignore exception for flexible ad if load failed
        catch (LoadAdException ex) {

            VhLogger.Instance.LogInformation(ex, "Could not load any interstitial ad.");
            return null;
        }
    }


    private readonly AsyncLock _showLock = new();
    public async Task<AdResult> Show(AdRequest adRequest, CancellationToken cancellationToken)
    {
        using var lockAsync = await _showLock.LockAsync(cancellationToken).VhConfigureAwait();
        try {

            client.EnablePassthruInProcessPackets(true);
            AdRequestTaskCompletionSource = new TaskCompletionSource<AdResult>();
            AdRequest = adRequest;
            var adResult = await AdRequestTaskCompletionSource.Task.VhWait(cancellationToken).VhConfigureAwait();
            _ = Task.Delay(2000, CancellationToken.None)
                .ContinueWith(_ => client.EnablePassthruInProcessPackets(false), CancellationToken.None); // not cancellation

            // Send RewardedAd result to the server
            if (adRequest.AdRequestType == AdRequestType.Rewarded) {
                // Check if the adResult is valid for the RewardedAd
                if (string.IsNullOrEmpty(adResult.AdData))
                    throw new InvalidOperationException("There is no AdData in the result RewardedAd.");

                // Send the rewarded ad result to the server to update the state
                await SendRewardedAdResult(adResult.AdData, cancellationToken).VhConfigureAwait();
            }

            return adResult;
        }
        catch {
            client.EnablePassthruInProcessPackets(false);
            throw;
        }
        finally {
            AdRequest = null;
            AdRequestTaskCompletionSource = null;
        }
    }

    public async Task SendRewardedAdResult(string adData, CancellationToken cancellationToken)
    {
        // request reward from server
        await using var requestResult = await client.SendRequest<SessionResponse>(
                new RewardedAdRequest {
                    RequestId = Guid.NewGuid() + ":client",
                    SessionId = client.SessionId,
                    SessionKey = client.SessionKey,
                    AdData = adData,
                },
        cancellationToken)
            .VhConfigureAwait();
    }
}
