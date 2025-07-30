using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Services.Ads;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Device.UiContexts;
using VpnHood.Core.Client.VpnServices.Manager;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib;

public class AppAdManager(
    AppAdService adService,
    VpnServiceManager vpnServiceManager,
    TimeSpan extendByRewardedAdThreshold)
{
    public bool IsShowing { get; private set; }
    public AppAdService AdService => adService;

    public async Task ShowAd(
        string sessionId,
        AdRequirement adRequirement,
        CancellationToken cancellationToken)
    {
        if (adRequirement == AdRequirement.None)
            return;

        // check if ad is already showing
        if (IsShowing)
            throw new InvalidOperationException("An ad is already being shown.");

        VhLogger.Instance.LogDebug("Showing ad. Session: {SessionId}, AdRequirement: {AdRequirement}", sessionId, adRequirement);

        // let's throw an exception if context is not set
        var uiContext = AppUiContext.Context;
        if (uiContext == null)
            throw new ShowAdNoUiException();

        // set wait for ad state
        await vpnServiceManager.SetWaitForAd(cancellationToken);

        try {
            IsShowing = true;

            // flexible ad
            var adResult = adRequirement switch {
                AdRequirement.Flexible => await adService
                    .ShowInterstitial(uiContext, sessionId, cancellationToken).Vhc(),
                AdRequirement.Rewarded => await adService.ShowRewarded(uiContext, sessionId, cancellationToken).Vhc(),
                _ => throw new AdException("Unsupported ad requirement.")
            };

            // rewarded ad
            VhLogger.Instance.LogDebug("Successfully loaded ad. AdResult: {NetworkName}", adResult.NetworkName);
            await vpnServiceManager.SetAdResult(adResult, adRequirement == AdRequirement.Rewarded, cancellationToken);
        }
        catch (LoadAdException ex) when (adRequirement == AdRequirement.Flexible) {
            await vpnServiceManager.SetAdResult(ex, cancellationToken);
            VhLogger.Instance.LogDebug("Could not load any flexible ad.");
        }
        catch (Exception ex) {
            await vpnServiceManager.SetAdResult(ex, cancellationToken);
            throw;
        }
        finally {
            IsShowing = false;
        }
    }

    public bool CanExtendByRewardedAd {
        get {
            var connectionInfo = vpnServiceManager.ConnectionInfo;

            return
                connectionInfo.SessionStatus is { CanExtendByRewardedAd: true } &&
                connectionInfo.SessionStatus.SessionExpirationTime > FastDateTime.UtcNow + extendByRewardedAdThreshold &&
                AdService.CanShowRewarded;
        }
    }

    public async Task ExtendByRewardedAd(CancellationToken cancellationToken)
    {
        // save variable to prevent null reference exception
        var connectionInfo = vpnServiceManager.ConnectionInfo;

        if (AdService.CanShowRewarded != true)
            throw new InvalidOperationException("Rewarded ad is not supported in this app.");

        if (connectionInfo.SessionInfo == null || connectionInfo is not { ClientState: ClientState.Connected })
            throw new InvalidOperationException("Could not show ad. The VPN is not connected.");

        if (!CanExtendByRewardedAd)
            throw new InvalidOperationException("Could not extend this session by a rewarded ad at this time.");

        // set wait for ad state
        await ShowAd(connectionInfo.SessionInfo.SessionId, AdRequirement.Rewarded, cancellationToken);
    }
}