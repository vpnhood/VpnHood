using Ga4.Trackers;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Abstractions.AdExceptions;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Device.UiContexts;
using VpnHood.Core.Common.IpLocations;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Services.Ads;

public class AppAdService(
    IRegionProvider regionProvider,
    AppAdProviderItem[] adProviderItems,
    TimeSpan loadAdTimeout,
    TimeSpan loadAdPostDelay,
    ITracker tracker)
{
    private AppCompositeAdService? _currentCompositeAdService;
    private readonly AppCompositeAdService _compositeInterstitialAdService = new(
        adProviderItems.Where(x => x.AdProvider.AdType == AppAdType.InterstitialAd).ToArray(),
        tracker);

    private readonly AppCompositeAdService _compositeRewardedAdService = new(
        adProviderItems.Where(x => x.AdProvider.AdType == AppAdType.RewardedAd).ToArray(),
        tracker);

    private InternalInAdProvider? ActiveInternalAdProvider => (InternalInAdProvider?)adProviderItems
        .FirstOrDefault(x => x.AdProvider is InternalInAdProvider { IsWaitingForAd: true })?
        .AdProvider;

    public int? LoadAdProgress {
        get {
            var endTime = _currentCompositeAdService?.LoadingAdEndTime;
            var startedTime = _currentCompositeAdService?.LoadingAdStartedTime;
            if (endTime is null || startedTime is null || FastDateTime.Now > endTime)
                return null;

            var pastTime = FastDateTime.Now - startedTime;
            var totalTime = endTime - startedTime;
            return (int)(pastTime.Value.TotalMilliseconds * 100 / totalTime.Value.TotalMilliseconds);
        }
    }

    public bool IsWaitingForInternalAd => ActiveInternalAdProvider != null;
    public void InternalAdDismiss(ShowAdResult showAdResult) => ActiveInternalAdProvider?.Dismiss(showAdResult); 
    public void InternalAdError(Exception exception) => ActiveInternalAdProvider?.SetException(exception);

    public void EnableAdProvider(string providerName, bool value)
    {
        var item = adProviderItems.FirstOrDefault(x => string.Equals(x.ProviderName, providerName, StringComparison.OrdinalIgnoreCase) || string.Equals(x.AdProvider.NetworkName, providerName, StringComparison.OrdinalIgnoreCase));
        if (item != null)
            item.IsEnabled = value;
    }

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

    public bool CanShowInterstitial => adProviderItems.Any(x => x.AdProvider.AdType == AppAdType.InterstitialAd);
    public bool CanShowRewarded => adProviderItems.Any(x => x.AdProvider.AdType == AppAdType.RewardedAd);

    public async Task LoadInterstitialAd(IUiContext uiContext, bool useFallback, CancellationToken cancellationToken)
    {
        // don't use VPN for loading ad
        var countryCode = await regionProvider.GetClientCountryCodeAsync(allowVpnServer: false, cancellationToken).Vhc();
        await LoadAd(_compositeInterstitialAdService, uiContext, isPreload: true,
            countryCode: countryCode, useFallback: useFallback,
            cancellationToken: cancellationToken).Vhc();
    }

    public Task<AdResult> ShowInterstitial(IUiContext uiContext, string sessionId,
        bool useFallback, CancellationToken cancellationToken)
    {
        return ShowAd(_compositeInterstitialAdService,
            uiContext, sessionId, useFallback: useFallback, cancellationToken);
    }

    public Task<AdResult> ShowRewarded(IUiContext uiContext, string sessionId,
        bool useFallback, CancellationToken cancellationToken)
    {
        if (!CanShowRewarded)
            throw new InvalidOperationException("Rewarded ad is not supported in this app.");

        return ShowAd(_compositeRewardedAdService,
            uiContext, sessionId, useFallback: useFallback, cancellationToken);
    }

    private async Task LoadAd(AppCompositeAdService appCompositeAdService, IUiContext uiContext,
        bool isPreload, string countryCode, bool useFallback, CancellationToken cancellationToken)
    {
        _currentCompositeAdService = appCompositeAdService;
        await appCompositeAdService.LoadAd(uiContext, isPreload: isPreload,
            countryCode: countryCode, loadAdTimeout: loadAdTimeout, useFallback: useFallback,
            forceReload: false,
            cancellationToken: cancellationToken).Vhc();

        // apply delay to prevent showing ads immediately after loading
        await Task.Delay(loadAdPostDelay, cancellationToken).Vhc();
    }

    private async Task<AdResult> ShowAd(AppCompositeAdService appCompositeAdService,
        IUiContext uiContext, string sessionId, bool useFallback, CancellationToken cancellationToken)
    {
        string? countryCode = null;
        try {
            countryCode = await regionProvider.GetClientCountryCodeAsync(allowVpnServer: false, cancellationToken).Vhc();

            // Load and sw ad
            var result = await LoadAndShowAd(appCompositeAdService, uiContext, sessionId, countryCode,
                useFallback: useFallback,
                cancellationToken: cancellationToken).Vhc();

            // enforce active UI context
            await AppAdUtils.VerifyActiveUi(uiContext, immediately: false).Vhc();

            // track ad show success
            await tracker.TryTrackWithCancellation(
                AppTrackerBuilder.BuildShowAdOk(adNetwork: result.NetworkName,
                    countryCode: countryCode, 
                    isPreload: appCompositeAdService.IsPreload,
                    showAdResult: result.IsClicked ? ShowAdResult.Clicked : ShowAdResult.Closed),
                cancellationToken).Vhc();

            return result;
        }
        catch (Exception ex) {
            var adNetwork = ex is ShowAdException showAdException ? showAdException.AdNetworkName : null;
            await tracker.TryTrackWithCancellation(AppTrackerBuilder.BuildAdFailed(
                    adNetwork: adNetwork, errorMessage: ex.Message, countryCode: countryCode,
                    isPreload: appCompositeAdService.IsPreload), cancellationToken).Vhc();

            // convert it to ShowAdNoUiException
            if (ex is not ShowAdNoUiException && !await AppAdUtils.IsActiveUi(uiContext)) {
                var noUiException = new ShowAdNoUiException();
                await tracker.TryTrackWithCancellation(AppTrackerBuilder.BuildShowAdFailed(
                        errorMessage: noUiException.Message, countryCode: countryCode,
                        isPreload: appCompositeAdService.IsPreload, adNetwork: adNetwork), cancellationToken).Vhc();
                throw noUiException;
            }

            throw;
        }
    }

    private async Task<AdResult> LoadAndShowAd(AppCompositeAdService appCompositeAdService,
        IUiContext uiContext, string sessionId, string countryCode, bool useFallback, CancellationToken cancellationToken)
    {

        // load ad if not loaded
        await LoadAd(appCompositeAdService, uiContext, isPreload: false,
            countryCode: countryCode, useFallback: useFallback, cancellationToken: cancellationToken).Vhc();

        // show ad
        try {
            var adData = $"sid:{sessionId};ad:{Guid.NewGuid()}";
            var result = await appCompositeAdService.ShowLoadedAd(uiContext, adData, cancellationToken).Vhc();

            var addResult = new AdResult {
                AdData = adData,
                IsClicked = result.ShowAdResult == ShowAdResult.Clicked,
                NetworkName = result.NetworkName
            };
            return addResult;
        }
        catch (Exception ex) {
            // track failed ad show
            var adNetwork = ex is ShowAdException showAdException ? showAdException.AdNetworkName : null;
            await tracker.TryTrackWithCancellation(AppTrackerBuilder.BuildShowAdFailed(adNetwork: adNetwork,
                errorMessage: ex.Message, countryCode: countryCode, isPreload: appCompositeAdService.IsPreload),
                cancellationToken).Vhc();

            throw;
        }
    }
}