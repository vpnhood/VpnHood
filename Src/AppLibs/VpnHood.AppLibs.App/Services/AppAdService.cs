using VpnHood.AppLibs.Abstractions;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Common.IpLocations;

namespace VpnHood.AppLibs.Services;

public class AppAdService(
    IRegionProvider regionProvider,
    AppAdProviderItem[] adProviderItems,
    AppAdOptions adOptions) :
    IAdService
{
    private readonly AppCompositeAdService _compositeInterstitialAdService =
        new(adProviderItems.Where(x => x.AdProvider.AdType == AppAdType.InterstitialAd).ToArray(), adOptions);

    private readonly AppCompositeAdService _compositeRewardedAdService =
        new(adProviderItems.Where(x => x.AdProvider.AdType == AppAdType.RewardedAd).ToArray(), adOptions);

    public bool CanShowRewarded => adProviderItems.Any(x => x.AdProvider.AdType == AppAdType.RewardedAd);
    public bool IsPreloadAdEnabled => adOptions.PreloadAd;

    public async Task LoadAd(IUiContext uiContext, CancellationToken cancellationToken)
    {
        var countryCode = await regionProvider.GetCurrentCountryAsync(cancellationToken);
        await _compositeInterstitialAdService.LoadAd(uiContext, countryCode: countryCode, forceReload: false,
            cancellationToken);
    }

    public Task<ShowedAdResult> ShowInterstitial(IUiContext uiContext, string sessionId,
        CancellationToken cancellationToken)
    {
        return ShowAd(_compositeInterstitialAdService, uiContext, sessionId, cancellationToken);
    }

    public Task<ShowedAdResult> ShowRewarded(IUiContext uiContext, string sessionId,
        CancellationToken cancellationToken)
    {
        if (!CanShowRewarded)
            throw new InvalidOperationException("Rewarded ad is not supported in this app.");

        return ShowAd(_compositeRewardedAdService, uiContext, sessionId, cancellationToken);
    }


    private async Task<ShowedAdResult> ShowAd(AppCompositeAdService appCompositeAdService,
        IUiContext uiContext, string sessionId, CancellationToken cancellationToken)
    {
        var adData = $"sid:{sessionId};ad:{Guid.NewGuid()}";
        var countryCode = await regionProvider.GetCurrentCountryAsync(cancellationToken);
        await appCompositeAdService.LoadAd(uiContext, countryCode: countryCode, forceReload: false, cancellationToken);
        var networkName = await appCompositeAdService.ShowLoadedAd(uiContext, adData, cancellationToken);
        var showAdResult = new ShowedAdResult {
            AdData = adData,
            NetworkName = networkName
        };
        return showAdResult;

    }
}