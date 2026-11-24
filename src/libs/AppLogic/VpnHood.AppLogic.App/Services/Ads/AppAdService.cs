using Ga4.Trackers;
using VpnHood.AppLogic.Abstractions;
using VpnHood.AppLogic.Api.Exceptions;
using VpnHood.AppLogic.Abstractions.Ads;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Monitoring;
using VpnHood.AppLogic.Abstractions.Ads.AdExceptions;

namespace VpnHood.AppLogic.App.Services.Ads;

internal class AppAdService(
    IReadOnlyList<AppAdProviderItem> adProviderItems,
    TimeSpan loadAdTimeout,
    TimeSpan loadAdPostDelay,
    ITracker tracker)
{
    private AppCompositeAdService? _currentCompositeAdService;

    private readonly AppCompositeAdService _compositeInterstitialAdService = new(
        adProviderItems.Where(x => x.AdProvider.AdType == AdType.InterstitialAd).ToArray(),
        loadAdTimeout, tracker);

    // the person asked to watch it, so the app never cuts its load (AppAdOptions.LoadAdTimeout)
    private readonly AppCompositeAdService _compositeRewardedAdService = new(
        adProviderItems.Where(x => x.AdProvider.AdType == AdType.RewardedAd).ToArray(),
        Timeout.InfiniteTimeSpan, tracker);

    private InternalInAdProvider? ActiveInternalAdProvider => (InternalInAdProvider?)adProviderItems
        .FirstOrDefault(x => x.AdProvider is InternalInAdProvider { IsWaitingForAd: true })?
        .AdProvider;

    public ProgressStatus? LoadAdProgress => _currentCompositeAdService?.LoadAdProgress;

    public bool IsWaitingForInternalAd => ActiveInternalAdProvider != null;
    public void InternalAdDismiss(ShowAdResult showAdResult) => ActiveInternalAdProvider?.Dismiss(showAdResult);
    public void InternalAdError(Exception exception) => ActiveInternalAdProvider?.SetException(exception);

    public void EnableAdProvider(string providerName, bool value)
    {
        var item = adProviderItems.FirstOrDefault(x => x.HasName(providerName));
        if (item != null)
            item.IsEnabled = value;
    }

    public bool CanShowInterstitial => adProviderItems.Any(x => x.AdProvider.AdType == AdType.InterstitialAd);
    public bool CanShowRewarded => adProviderItems.Any(x => x.AdProvider.AdType == AdType.RewardedAd);

    // Over the VPN where a network but the house ad can show that kind of ad where the person is: the session's last try
    // then goes through the tunnel, and only such networks load there (AdRoute.OverVpn). Asked of the loaders, with
    // their own copies of the list, so the session is told what they load
    public bool CanShowInterstitialOverVpn =>
        _compositeInterstitialAdService.CanFill(AdRoute.OverVpn, isLastRoute: false, CountryCode);

    public bool CanShowRewardedOverVpn =>
        _compositeRewardedAdService.CanFill(AdRoute.OverVpn, isLastRoute: false, CountryCode);

    // a rewarded ad where the person is: a network that serves their country, the house ad too
    public bool CanShowRewardedInRegion =>
        _compositeRewardedAdService.CanFill(AdRoute.Passthrough, isLastRoute: true, CountryCode);

    private static string CountryCode => AppRegionInfo.CurrentRegion.Name;

    // the next connect's ad, ahead of its first try, which comes before the VPN
    public async Task LoadInterstitialAd(IUiContext uiContext, CancellationToken cancellationToken)
    {
        _currentCompositeAdService = _compositeInterstitialAdService;
        await _compositeInterstitialAdService.PreloadAd(uiContext, CountryCode, AdRoute.BeforeVpn, isLastRoute: false,
            cancellationToken).Vhc();

        // apply delay to prevent showing ads immediately after loading
        await Task.Delay(loadAdPostDelay, cancellationToken).Vhc();
    }

    public Task<AdResult> ShowInterstitial(IUiContext uiContext, string sessionId,
        AdRoute route, bool isLastRoute, CancellationToken cancellationToken)
    {
        return ShowAd(_compositeInterstitialAdService,
            uiContext, sessionId, route, isLastRoute, cancellationToken);
    }

    public Task<AdResult> ShowRewarded(IUiContext uiContext, string sessionId,
        AdRoute route, bool isLastRoute, CancellationToken cancellationToken)
    {
        if (!CanShowRewarded)
            throw new InvalidOperationException("Rewarded ad is not supported in this app.");

        return ShowAd(_compositeRewardedAdService,
            uiContext, sessionId, route, isLastRoute, cancellationToken);
    }

    private async Task<AdResult> ShowAd(AppCompositeAdService appCompositeAdService,
        IUiContext uiContext, string sessionId, AdRoute route, bool isLastRoute, CancellationToken cancellationToken)
    {
        string? countryCode = null;
        try {
            countryCode = AppRegionInfo.CurrentRegion.Name;

            // Load and sw ad
            var result = await LoadAndShowAd(appCompositeAdService, uiContext, sessionId, countryCode,
                route, isLastRoute, cancellationToken).Vhc();

            // enforce active UI context
            await AppAdUtils.VerifyActiveUi(uiContext, immediately: false).Vhc();

            // track ad show success
            await tracker.TryTrack(
                AppTrackerBuilder.BuildShowAdOk(adNetwork: result.NetworkName,
                    countryCode: countryCode,
                    isPreload: appCompositeAdService.IsPreload,
                    showAdResult: result.IsClicked ? ShowAdResult.Clicked : ShowAdResult.Closed),
                cancellationToken).Vhc();

            return result;
        }
        catch (Exception ex) {
            var adNetwork = ex is ShowAdException showAdException ? showAdException.AdNetworkName : null;
            await tracker.TryTrack(AppTrackerBuilder.BuildAdFailed(
                adNetwork: adNetwork, errorMessage: ex.Message, countryCode: countryCode,
                isPreload: appCompositeAdService.IsPreload), cancellationToken).Vhc();

            // convert it to ShowAdNoUiException
            if (ex is not ShowAdNoUiException && !await AppAdUtils.IsActiveUi(uiContext).Vhc()) {
                var noUiException = new ShowAdNoUiException();
                await tracker.TryTrack(AppTrackerBuilder.BuildShowAdFailed(
                    errorMessage: noUiException.Message, countryCode: countryCode,
                    isPreload: appCompositeAdService.IsPreload, adNetwork: adNetwork), cancellationToken).Vhc();
                throw noUiException;
            }

            throw;
        }
    }

    // the loader tracks each show that failed
    private async Task<AdResult> LoadAndShowAd(AppCompositeAdService appCompositeAdService,
        IUiContext uiContext, string sessionId, string countryCode, AdRoute route, bool isLastRoute,
        CancellationToken cancellationToken)
    {
        _currentCompositeAdService = appCompositeAdService;
        var adData = $"sid:{sessionId};ad:{Guid.NewGuid()}";
        var result = await appCompositeAdService.LoadAndShowAd(uiContext, adData, countryCode, route, isLastRoute,
            loadAdPostDelay, cancellationToken).Vhc();

        return new AdResult {
            AdData = adData,
            IsClicked = result.ShowAdResult == ShowAdResult.Clicked,
            NetworkName = result.NetworkName
        };
    }
}