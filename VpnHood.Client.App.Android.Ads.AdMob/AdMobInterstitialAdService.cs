using Android.Gms.Ads;
using Android.Gms.Ads.Interstitial;
using VpnHood.Client.App.Abstractions;
using VpnHood.Client.App.Exceptions;
using VpnHood.Client.Device;
using VpnHood.Client.Device.Droid;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Utils;

namespace VpnHood.Client.App.Droid.Ads.VhAdMob;

public class AdMobInterstitialAdService(string adUnitId, bool hasVideo) : IAppAdService
{
    private InterstitialAd? _loadedAd;
    public string NetworkName => "AdMob";
    public AppAdType AdType => AppAdType.InterstitialAd;
    public DateTime? AdLoadedTime {get; private set; }
    public TimeSpan AdLifeSpan => AdMobUtil.DefaultAdTimeSpan;

    public static AdMobInterstitialAdService Create(string adUnitId, bool hasVideo)
    {
        var ret = new AdMobInterstitialAdService(adUnitId, hasVideo);
        return ret;
    }

    public bool IsCountrySupported(string countryCode)
    {
        // Make sure it is upper case
        countryCode = countryCode.Trim().ToUpper(); 

        // these countries are not supported at all
        if (countryCode == "CN")
            return false;

        // these countries video ad is not supported
        if (hasVideo)
            return countryCode != "IR";

        return true;
    }


    public async Task LoadAd(IUiContext uiContext, CancellationToken cancellationToken)
    {
        var appUiContext = (AndroidUiContext)uiContext;
        var activity = appUiContext.Activity;
        if (activity.IsDestroyed)
            throw new LoadAdException("MainActivity has been destroyed before loading the ad.");

        // initialize
        await AdMobUtil.Initialize(activity, cancellationToken);

        // reset the last loaded ad
        AdLoadedTime = null;
        _loadedAd = null;

        var adLoadCallback = new MyInterstitialAdLoadCallback();
        var adRequest = new AdRequest.Builder().Build();
        // AdMob load ad must call from main thread
        activity.RunOnUiThread(() => InterstitialAd.Load(activity, adUnitId, adRequest, adLoadCallback));

        var cancellationTask = new TaskCompletionSource();
        cancellationToken.Register(cancellationTask.SetResult);
        await Task.WhenAny(adLoadCallback.Task, cancellationTask.Task).VhConfigureAwait();
        cancellationToken.ThrowIfCancellationRequested();

        _loadedAd = await adLoadCallback.Task.VhConfigureAwait();
        AdLoadedTime = DateTime.Now;
    }

    public async Task ShowAd(IUiContext uiContext, string? customData, CancellationToken cancellationToken)
    {
        var appUiContext = (AndroidUiContext)uiContext;
        var activity = appUiContext.Activity;
        if (activity.IsDestroyed)
            throw new ShowAdException("MainActivity has been destroyed before showing the ad.");

        if (_loadedAd == null)
            throw new ShowAdException($"The {AdType} has not been loaded.");

        try
        {
            var fullScreenContentCallback = new MyFullScreenContentCallback();
            activity.RunOnUiThread(() =>
            {
                _loadedAd.FullScreenContentCallback = fullScreenContentCallback;
                _loadedAd.Show(activity);
            });

            // wait for show or dismiss
            var cancellationTask = new TaskCompletionSource();
            cancellationToken.Register(cancellationTask.SetResult);
            await Task.WhenAny(fullScreenContentCallback.DismissedTask, cancellationTask.Task).VhConfigureAwait();
            cancellationToken.ThrowIfCancellationRequested();

            // check task errors
            if (fullScreenContentCallback.DismissedTask.IsFaulted)
                throw fullScreenContentCallback.DismissedTask.Exception;
        }
        finally
        {
            _loadedAd = null;
            AdLoadedTime = null;
        }

    }

    private class MyInterstitialAdLoadCallback : AdNetworkCallBackFix.InterstitialAdLoadCallback
    {
        private readonly TaskCompletionSource<InterstitialAd> _loadedCompletionSource = new();
        public Task<InterstitialAd> Task => _loadedCompletionSource.Task;

        protected override void OnAdLoaded(InterstitialAd interstitialAd)
        {
            _loadedCompletionSource.TrySetResult(interstitialAd);
        }

        public override void OnAdFailedToLoad(LoadAdError addError)
        {
            _loadedCompletionSource.TrySetException(
                addError.Message.Contains("No fill.", StringComparison.OrdinalIgnoreCase)
                    ? new NoFillAdException(addError.Message)
                    : new LoadAdException(addError.Message));
        }
    }

    private class MyFullScreenContentCallback : FullScreenContentCallback
    {
        private readonly TaskCompletionSource _dismissedCompletionSource = new();
        public Task DismissedTask => _dismissedCompletionSource.Task;

        public override void OnAdDismissedFullScreenContent()
        {
            _dismissedCompletionSource.TrySetResult();
        }

        public override void OnAdFailedToShowFullScreenContent(AdError adError)
        {
            _dismissedCompletionSource.TrySetException(new ShowAdException(adError.Message));
        }
    }

    public void Dispose()
    {
    }
}