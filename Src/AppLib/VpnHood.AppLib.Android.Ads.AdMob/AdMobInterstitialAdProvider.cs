using Google.Android.Gms.Ads;
using Google.Android.Gms.Ads.Interstitial;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Abstractions.AdExceptions;
using VpnHood.AppLib.Droid.Ads.VhAdMob.AdNetworkCallBackShim;
using VpnHood.Core.Client.Device.Droid;
using VpnHood.Core.Client.Device.Droid.Utils;
using VpnHood.Core.Client.Device.UiContexts;

namespace VpnHood.AppLib.Droid.Ads.VhAdMob;

public class AdMobInterstitialAdProvider(string adUnitId) : IAppAdProvider
{
    private InterstitialAd? _loadedAd;
    public string NetworkName => "AdMob";
    public AppAdType AdType => AppAdType.InterstitialAd;
    public DateTime? AdLoadedTime { get; private set; }
    public TimeSpan AdLifeSpan => AdMobUtil.DefaultAdTimeSpan;

    public static AdMobInterstitialAdProvider Create(string adUnitId)
    {
        var ret = new AdMobInterstitialAdProvider(adUnitId);
        return ret;
    }

    public async Task LoadAd(IUiContext uiContext, CancellationToken cancellationToken)
    {
        var appUiContext = (AndroidUiContext)uiContext;
        var activity = appUiContext.Activity;
        if (activity.IsDestroyed)
            throw new LoadAdException("MainActivity has been destroyed before loading the ad.");

        // initialize (AdMob do it for first time, so its will throw better exception)
        await AdMobUtil.Initialize(activity, cancellationToken).ConfigureAwait(false);

        // reset the last loaded ad
        AdLoadedTime = null;
        _loadedAd = null;

        var adLoadCallback = new MyInterstitialAdLoadCallback();
        var adRequest = new AdRequest.Builder().Build();

        // AdMob load ad must call from main thread
        await AndroidUtil.RunOnUiThread(activity,
                () => InterstitialAd.Load(activity, adUnitId, adRequest, adLoadCallback))
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);


        _loadedAd = await adLoadCallback.Task
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        AdLoadedTime = DateTime.Now;
    }

    public async Task<ShowAdResult> ShowAd(IUiContext uiContext, string? customData,
        CancellationToken cancellationToken)
    {
        var appUiContext = (AndroidUiContext)uiContext;
        var activity = appUiContext.Activity;
        if (activity.IsDestroyed)
            throw new ShowAdException("MainActivity has been destroyed before showing the ad.");

        if (_loadedAd == null)
            throw new ShowAdException($"The {AdType} has not been loaded.");

        try {
            var fullScreenContentCallback = new AdMobFullScreenContentCallback();
            await AndroidUtil
                .RunOnUiThread(activity, () => {
                    _loadedAd.FullScreenContentCallback = fullScreenContentCallback;
                    _loadedAd.Show(activity);
                })
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            // check task errors
            var result = await fullScreenContentCallback.DismissedTask
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            return result;
        }
        finally {
            _loadedAd = null;
            AdLoadedTime = null;
        }
    }

    private class MyInterstitialAdLoadCallback : InterstitialAdLoadCallbackShim
    {
        private readonly TaskCompletionSource<InterstitialAd> _loadedCompletionSource = new();
        public Task<InterstitialAd> Task => _loadedCompletionSource.Task;

        protected override void OnAdLoaded(InterstitialAd interstitialAd)
        {
            _loadedCompletionSource.TrySetResult(interstitialAd);
        }

        public override void OnAdFailedToLoad(LoadAdError adError)
        {
            var message = string.IsNullOrWhiteSpace(adError.Message) ? "AdMob load empty message." : adError.Message;
            _loadedCompletionSource.TrySetException(
                adError.Message.Contains("No fill.", StringComparison.OrdinalIgnoreCase)
                    ? new NoFillAdException(message)
                    : new LoadAdException(message));
        }
    }

    public void Dispose()
    {
    }
}