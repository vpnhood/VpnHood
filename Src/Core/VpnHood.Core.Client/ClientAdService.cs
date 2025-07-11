using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.Utils;

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
        catch (ShowAdNoUiException) {
            throw; // No UI exception should be propagated and prevent session start
        }
        // ignore exception for flexible ad if load failed
        catch (Exception ex) {
            VhLogger.Instance.LogInformation(ex, "Could not show any interstitial ad.");
            return null;
        }
    }


    private readonly AsyncLock _showLock = new();

    private async Task<AdResult> Show(AdRequest adRequest, CancellationToken cancellationToken)
    {
        using var lockAsync = await _showLock.LockAsync(cancellationToken).Vhc();
        try {
            AdRequestTaskCompletionSource = new TaskCompletionSource<AdResult>();
            AdRequest = adRequest;
            var adResult = await AdRequestTaskCompletionSource.Task.WaitAsync(cancellationToken).Vhc();

            // Send RewardedAd result to the server
            if (adRequest.AdRequestType == AdRequestType.Rewarded) {
                // Check if the adResult is valid for the RewardedAd
                if (string.IsNullOrEmpty(adResult.AdData))
                    throw new InvalidOperationException("There is no AdData in the result RewardedAd.");

                // Send the rewarded ad result to the server to update the state
                await SendRewardedAdResult(adResult.AdData, cancellationToken).Vhc();
            }

            return adResult;
        }
        finally {
            AdRequest = null;
            AdRequestTaskCompletionSource = null;
        }
    }

    public async Task SendRewardedAdResult(string adData, CancellationToken cancellationToken)
    {
        // request reward from server
        using var requestResult = await client.SendRequest<SessionResponse>(
                new RewardedAdRequest {
                    RequestId = UniqueIdFactory.Create(),
                    SessionId = client.SessionId,
                    SessionKey = client.SessionKey,
                    AdData = adData
                },
                cancellationToken).Vhc();
    }
}