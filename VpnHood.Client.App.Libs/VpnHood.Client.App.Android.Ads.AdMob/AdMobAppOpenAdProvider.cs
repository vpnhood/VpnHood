using Android.Gms.Ads;
using Android.Gms.Ads.AppOpen;
using VpnHood.Client.App.Abstractions;
using VpnHood.Client.App.Droid.Ads.VhAdMob.AdNetworkCallBackFix;
using VpnHood.Client.App.Exceptions;
using VpnHood.Client.Device;
using VpnHood.Client.Device.Droid;
using VpnHood.Client.Device.Droid.Utils;
using VpnHood.Common.Exceptions;

namespace VpnHood.Client.App.Droid.Ads.VhAdMob;

public class AdMobAppOpenAdProvider(string adUnitId) : IAppAdProvider
{
    private AppOpenAd? _loadedAd;
    public string NetworkName => "AdMob";
    public AppAdType AdType => AppAdType.AppOpenAd;
    public DateTime? AdLoadedTime { get; private set; }
    public TimeSpan AdLifeSpan => AdMobUtil.DefaultAdTimeSpan;

    public static AdMobAppOpenAdProvider Create(string adUnitId)
    {
        var ret = new AdMobAppOpenAdProvider(adUnitId);
        return ret;
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

        var adLoadCallback = new MyAppOpenAdLoadCallback();
        var adRequest = new AdRequest.Builder().Build();
        await AndroidUtil.RunOnUiThread(activity, () => { AppOpenAd.Load(activity, adUnitId, adRequest, adLoadCallback); })
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        // handle cancellation
        _loadedAd = await adLoadCallback.Task
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

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

        try {
            var fullScreenContentCallback = new MyFullScreenContentCallback();
            await AndroidUtil
                .RunOnUiThread(activity, () => {
                        _loadedAd.FullScreenContentCallback = fullScreenContentCallback;
                        _loadedAd.Show(activity); })
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            // wait for show or dismiss
            await fullScreenContentCallback.DismissedTask
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally {
            _loadedAd = null;
            AdLoadedTime = null;
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

    private class MyAppOpenAdLoadCallback : AppOpenAdLoadCallback
    {
        private readonly TaskCompletionSource<AppOpenAd> _loadedCompletionSource = new();
        public Task<AppOpenAd> Task => _loadedCompletionSource.Task;

        protected override void OnAdLoaded(AppOpenAd appOpenAd)
        {
            _loadedCompletionSource.TrySetResult(appOpenAd);
        }

        public override void OnAdFailedToLoad(LoadAdError addError)
        {
            _loadedCompletionSource.TrySetException(
                addError.Message.Contains("No fill.", StringComparison.OrdinalIgnoreCase)
                    ? new NoFillAdException(addError.Message)
                    : new LoadAdException(addError.Message));
        }
    }

    public void Dispose()
    {
    }
}