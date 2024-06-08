using Android.Gms.Ads;
using Android.Gms.Ads.Interstitial;
using VpnHood.Client.App.Abstractions;
using VpnHood.Client.App.Droid.Ads.VhAdMob.AdNetworkCallBackOverride;
using VpnHood.Client.Device;
using VpnHood.Client.Device.Droid;
using VpnHood.Client.Exceptions;
using VpnHood.Common.Utils;

namespace VpnHood.Client.App.Droid.Ads.VhAdMob;

public class AdMobInterstitialAdService(string adUnitId) : IAppAdService
{
    private MyAdLoadCallback? _adLoadCallback;
    private DateTime _lastLoadAdTime = DateTime.MinValue;
    private InterstitialAd? _loadedAd;

    public static AdMobInterstitialAdService Create(string adUnitId)
    {
        var ret = new AdMobInterstitialAdService(adUnitId);
        return ret;
    }

    public string NetworkName => "AdMob";
    public AppAdType AdType => AppAdType.InterstitialAd;

    public async Task LoadAd(IUiContext uiContext, CancellationToken cancellationToken)
    {
        var appUiContext = (AndroidUiContext)uiContext;
        var activity = appUiContext.Activity;
        if (activity.IsDestroyed)
            throw new AdException("MainActivity has been destroyed before loading the ad.");

        // Ad already loaded
        if (_adLoadCallback != null && _lastLoadAdTime.AddHours(1) < DateTime.Now)
            _loadedAd = await _adLoadCallback.Task.VhConfigureAwait();

        // Load a new Ad
        try
        {
            _adLoadCallback = new MyAdLoadCallback();
            var adRequest = new AdRequest.Builder().Build();
            activity.RunOnUiThread(() => InterstitialAd.Load(activity, adUnitId, adRequest, _adLoadCallback));

            var cancellationTask = new TaskCompletionSource();
            cancellationToken.Register(cancellationTask.SetResult);
            await Task.WhenAny(_adLoadCallback.Task, cancellationTask.Task).VhConfigureAwait();
            cancellationToken.ThrowIfCancellationRequested();

            var interstitialAd = await _adLoadCallback.Task.VhConfigureAwait();
            _lastLoadAdTime = DateTime.Now;
            _loadedAd = interstitialAd;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _adLoadCallback = null;
            _lastLoadAdTime = DateTime.MinValue;
            if (ex is AdLoadException) throw;
            throw new AdLoadException($"Failed to load {AdType.ToString()}.", ex);
        }
    }

    public async Task ShowAd(IUiContext uiContext, string? customData, CancellationToken cancellationToken)
    {
        var appUiContext = (AndroidUiContext)uiContext;
        var activity = appUiContext.Activity;
        if (activity.IsDestroyed)
            throw new AdException("MainActivity has been destroyed before showing the ad.");
        
        if (_loadedAd == null)
            throw new AdException($"The {AdType.ToString()} has not benn loaded.");

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

        _adLoadCallback = null;
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
            _dismissedCompletionSource.TrySetException(new AdException(adError.Message));
        }
    }
    private class MyAdLoadCallback : AdMobInterstitialAdLoadCallback
    {
        private readonly TaskCompletionSource<InterstitialAd> _loadedCompletionSource = new();
        public Task<InterstitialAd> Task => _loadedCompletionSource.Task;

        protected override void OnAdLoaded(InterstitialAd interstitialAd)
        {
            _loadedCompletionSource.TrySetResult(interstitialAd);
        }

        public override void OnAdFailedToLoad(LoadAdError addError)
        {
            _loadedCompletionSource.TrySetException(new AdLoadException(addError.Message));
        }
    }

    public void Dispose()
    {
    }
}