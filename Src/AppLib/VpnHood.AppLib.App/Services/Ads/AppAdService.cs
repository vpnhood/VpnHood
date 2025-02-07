using Ga4.Trackers;
using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Client.Device.Exceptions;
using VpnHood.Core.Client.Manager;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.IpLocations;
using VpnHood.Core.Common.Utils;

namespace VpnHood.AppLib.Services.Ads;

public class AppAdService(
    IRegionProvider regionProvider,
    AppAdProviderItem[] adProviderItems,
    AppAdOptions adOptions,
    ITracker? tracker) :
    IAdService
{
    private readonly AppCompositeAdService _compositeInterstitialAdService =
        new(adProviderItems.Where(x => x.AdProvider.AdType == AppAdType.InterstitialAd).ToArray(), adOptions);

    private readonly AppCompositeAdService _compositeRewardedAdService =
        new(adProviderItems.Where(x => x.AdProvider.AdType == AppAdType.RewardedAd).ToArray(), adOptions);

    public bool CanShowInterstitial => adProviderItems.Any(x => x.AdProvider.AdType == AppAdType.InterstitialAd);
    public bool CanShowRewarded => adProviderItems.Any(x => x.AdProvider.AdType == AppAdType.RewardedAd);
    public bool IsPreloadAdEnabled => adOptions.PreloadAd;

    public async Task LoadAd(IUiContext uiContext, CancellationToken cancellationToken)
    {
        var countryCode = await regionProvider.GetCurrentCountryAsync(cancellationToken).VhConfigureAwait();
        await _compositeInterstitialAdService
            .LoadAd(uiContext, countryCode: countryCode, forceReload: false, cancellationToken)
            .VhConfigureAwait();
    }

    public Task<AdResult> ShowInterstitial(IUiContext uiContext, string sessionId,
        CancellationToken cancellationToken)
    {
        return ShowAd(_compositeInterstitialAdService, uiContext, sessionId, cancellationToken);
    }

    public Task<AdResult> ShowRewarded(IUiContext uiContext, string sessionId,
        CancellationToken cancellationToken)
    {
        if (!CanShowRewarded)
            throw new InvalidOperationException("Rewarded ad is not supported in this app.");

        return ShowAd(_compositeRewardedAdService, uiContext, sessionId, cancellationToken);
    }


    private async Task<AdResult> ShowAd(AppCompositeAdService appCompositeAdService,
        IUiContext uiContext, string sessionId, CancellationToken cancellationToken)
    {
        try {
            var adData = $"sid:{sessionId};ad:{Guid.NewGuid()}";
            var countryCode = await regionProvider.GetCurrentCountryAsync(cancellationToken);
            await appCompositeAdService.LoadAd(uiContext, countryCode: countryCode, forceReload: false, cancellationToken);
            var networkName = await appCompositeAdService.ShowLoadedAd(uiContext, adData, cancellationToken);
            var showAdResult = new AdResult {
                AdData = adData,
                NetworkName = networkName
            };

            var trackEvent = ClientTrackerBuilder.BuildShowAdStatus(networkName);
            _ = tracker?.Track(trackEvent);
            return showAdResult;
        }
        catch (Exception ex) {
            var trackEvent = ClientTrackerBuilder.BuildShowAdStatus("all", ex.Message);
            _ = tracker?.Track(trackEvent);

            if (ex is UiContextNotAvailableException)
                throw new ShowAdNoUiException();

            throw;
        }
    }
}