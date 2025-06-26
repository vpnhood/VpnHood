using Ga4.Trackers;
using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Client.Device.UiContexts;
using VpnHood.Core.Client.VpnServices.Manager;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.IpLocations;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Services.Ads;

public class AppAdService(
    IRegionProvider regionProvider,
    IDevice device,
    AppAdProviderItem[] adProviderItems,
    AppAdOptions adOptions,
    ITracker? tracker) :
    IAdService
{
    private readonly AppCompositeAdService _compositeInterstitialAdService =
        new(adProviderItems.Where(x => x.AdProvider.AdType == AppAdType.InterstitialAd)
            .ToArray());

    private readonly AppCompositeAdService _compositeRewardedAdService =
        new(adProviderItems.Where(x => x.AdProvider.AdType == AppAdType.RewardedAd)
            .ToArray());

    private readonly AppCompositeAdService _compositeInterstitialAdOverVpnService =
        new(adProviderItems.Where(x => x is { CanShowOverVpn: true, AdProvider.AdType: AppAdType.InterstitialAd })
            .ToArray());

    private readonly AppCompositeAdService _compositeRewardedAdOverVpnService =
        new(adProviderItems.Where(x => x is { CanShowOverVpn: true, AdProvider.AdType: AppAdType.RewardedAd })
            .ToArray());

    private bool CanShowOverVpn(AppAdType adType) =>
        adProviderItems.Any(x => x.AdProvider.AdType == adType && x.CanShowOverVpn);

    public bool CanShowOverVpn(AdRequestType adRequestType)
    {
        return adRequestType switch {
            AdRequestType.Interstitial => CanShowOverVpn(AppAdType.InterstitialAd),
            AdRequestType.Rewarded => CanShowOverVpn(AppAdType.RewardedAd),
            _ => throw new ArgumentOutOfRangeException(nameof(adRequestType), adRequestType, null)
        };
    }

    public TimeSpan ShowAdPostDelay => adOptions.ShowAdPostDelay;
    public bool CanShowInterstitial => adProviderItems.Any(x => x.AdProvider.AdType == AppAdType.InterstitialAd);
    public bool CanShowRewarded => adProviderItems.Any(x => x.AdProvider.AdType == AppAdType.RewardedAd);
    public bool IsPreloadAdEnabled => adOptions.PreloadAd;

    public async Task LoadInterstitialAdAd(IUiContext uiContext, CancellationToken cancellationToken)
    {
        try {
            // don't use VPN for loading ad
            device.TryBindProcessToVpn(false);
            await LoadAd(_compositeInterstitialAdService, uiContext, cancellationToken);
        }
        finally {
            device.TryBindProcessToVpn(true);
        }

    }

    public Task<AdResult> ShowInterstitial(IUiContext uiContext, string sessionId,
        CancellationToken cancellationToken)
    {
        return ShowAd(_compositeInterstitialAdService, uiContext, sessionId, allowOverVpn: false, cancellationToken);
    }

    public Task<AdResult> ShowRewarded(IUiContext uiContext, string sessionId,
        CancellationToken cancellationToken)
    {
        if (!CanShowRewarded)
            throw new InvalidOperationException("Rewarded ad is not supported in this app.");

        return ShowAd(_compositeRewardedAdService, uiContext, sessionId, allowOverVpn: false, cancellationToken);
    }

    private async Task LoadAd(AppCompositeAdService appCompositeAdService, IUiContext uiContext, CancellationToken cancellationToken)
    {
        var countryCode = await regionProvider.GetClientCountryCodeAsync(allowVpnServer: false, cancellationToken).Vhc();
        await appCompositeAdService.LoadAd(
                uiContext, countryCode: countryCode,
                forceReload: false, loadAdTimeout: adOptions.LoadAdTimeout, cancellationToken)
            .Vhc();

        // apply delay to prevent showing ads immediately after loading
        await Task.Delay(adOptions.LoadAdPostDelay, cancellationToken).Vhc();
    }

    private async Task<AdResult> ShowAd(AppCompositeAdService appCompositeAdService,
        IUiContext uiContext, string sessionId, bool allowOverVpn, CancellationToken cancellationToken)
    {
        try {
            // don't use VPN for the ad
            device.TryBindProcessToVpn(allowOverVpn);
            var result =  await ShowAdInternal(appCompositeAdService, uiContext, sessionId, cancellationToken);
            
            var trackEvent = AppTrackerBuilder.BuildShowAdStatus(result.NetworkName ?? "UnknownNetwork");
            _ = TryRestoreProcessVpn(trackEvent, adOptions.ShowAdPostDelay, cancellationToken);
            return result;
        }
        catch (Exception ex) {
            var trackEvent = AppTrackerBuilder.BuildShowAdStatus("all", ex.Message);
            _ = TryRestoreProcessVpn(trackEvent, TimeSpan.Zero, cancellationToken);
            throw;
        }
    }

    private async Task<AdResult> ShowAdInternal(AppCompositeAdService appCompositeAdService,
        IUiContext uiContext, string sessionId, CancellationToken cancellationToken)
    {
        try {
            // load ad if not loaded
            await LoadAd(appCompositeAdService, uiContext, cancellationToken);

            // show ad
            var adData = $"sid:{sessionId};ad:{Guid.NewGuid()}";
            var networkName = await appCompositeAdService.ShowLoadedAd(uiContext, adData, cancellationToken);
            var showAdResult = new AdResult {
                AdData = adData,
                NetworkName = networkName
            };

            //wait for finishing trackers
            return showAdResult;
        }
        catch (UiContextNotAvailableException ex) {
            throw new ShowAdNoUiException(ex);
        }
    }

    private async Task TryRestoreProcessVpn(TrackEvent trackEvent, TimeSpan delay, CancellationToken cancellationToken)
    {
        await device.TryBindProcessToVpn(true, delay, cancellationToken);
        if (tracker != null)
            await tracker.TryTrack(trackEvent);
    }
}