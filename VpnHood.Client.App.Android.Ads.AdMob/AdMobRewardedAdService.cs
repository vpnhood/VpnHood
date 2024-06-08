using Android.Gms.Ads;
using Android.Gms.Ads.Rewarded;
using VpnHood.Client.App.Abstractions;
using VpnHood.Client.App.Droid.Ads.VhAdMob.AdNetworkCallBackOverride;
using VpnHood.Client.Device;
using VpnHood.Client.Device.Droid;
using VpnHood.Client.Exceptions;
using VpnHood.Common.Utils;

namespace VpnHood.Client.App.Droid.Ads.VhAdMob;

public class AdMobRewardedAdService(string adUnitId) : IAppAdService
{
    private MyAdLoadCallback? _adLoadCallback;
    private DateTime _lastLoadAdTime = DateTime.MinValue;
    private RewardedAd? _loadedAd;

     public static AdMobRewardedAdService Create(string adUnitId)
    {
        var ret = new AdMobRewardedAdService(adUnitId);
        return ret;
    }
     
    public string NetworkName => "AdMob";
    public AppAdType AdType => AppAdType.RewardedAd;

    public async Task LoadAd(IUiContext uiContext, CancellationToken cancellationToken)
    {
        var appUiContext = (AndroidUiContext)uiContext;
        var activity = appUiContext.Activity;
        if (activity.IsDestroyed)
            throw new AdException("MainActivity has been destroyed before loading the ad.");
        
        // Ad already loaded
        if (_adLoadCallback != null && _lastLoadAdTime.AddHours(1) < DateTime.Now)
            _loadedAd =  await _adLoadCallback.Task.VhConfigureAwait();
        
        // Load a new Ad
        try
        {
            _adLoadCallback = new MyAdLoadCallback();
            var adRequest = new AdRequest.Builder().Build();
            activity.RunOnUiThread(() => RewardedAd.Load(activity, adUnitId, adRequest, _adLoadCallback));

            var cancellationTask = new TaskCompletionSource();
            cancellationToken.Register(cancellationTask.SetResult);
            await Task.WhenAny(_adLoadCallback.Task, cancellationTask.Task).VhConfigureAwait();
            cancellationToken.ThrowIfCancellationRequested();

            var rewardedAd = await _adLoadCallback.Task.VhConfigureAwait();
            _lastLoadAdTime = DateTime.Now;
            _loadedAd =  rewardedAd;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _adLoadCallback = null;
            _lastLoadAdTime = DateTime.MinValue;
            if (ex is AdLoadException) throw;
            throw new AdLoadException($"Failed to load {AdType}.", ex);
        }
    }

    public async Task ShowAd(IUiContext uiContext, string? customData, CancellationToken cancellationToken)
    {
        var appUiContext = (AndroidUiContext)uiContext;
        var activity = appUiContext.Activity;
        if (activity.IsDestroyed)
            throw new AdException("MainActivity has been destroyed before showing the ad.");
        
        if (_loadedAd == null)
            throw new AdException($"The {AdType} has not benn loaded.");

        // create ad custom data
        var verificationOptions = new ServerSideVerificationOptions.Builder()
            .SetCustomData(customData ?? "")
            .Build();

        var fullScreenContentCallback = new MyFullScreenContentCallback();
        var userEarnedRewardListener = new MyOnUserEarnedRewardListener();

        activity.RunOnUiThread(() =>
        {
            _loadedAd.SetServerSideVerificationOptions(verificationOptions);
            _loadedAd.FullScreenContentCallback = fullScreenContentCallback;
            _loadedAd.Show(activity, userEarnedRewardListener);
        });

        // wait for earn reward or dismiss
        var cancellationTask = new TaskCompletionSource();
        cancellationToken.Register(cancellationTask.SetResult);
        await Task.WhenAny(fullScreenContentCallback.DismissedTask, userEarnedRewardListener.UserEarnedRewardTask,
            cancellationTask.Task).VhConfigureAwait();
        cancellationToken.ThrowIfCancellationRequested();

        // check task errors
        if (fullScreenContentCallback.DismissedTask.IsFaulted)
            throw fullScreenContentCallback.DismissedTask.Exception;

        _adLoadCallback = null;
    }
    

    private class MyAdLoadCallback : AdMobRewardedAdLoadCallback
    {
        private readonly TaskCompletionSource<RewardedAd> _loadedCompletionSource = new();
        public Task<RewardedAd> Task => _loadedCompletionSource.Task;

        protected override void OnAdLoaded(RewardedAd rewardedAd)
        {
            _loadedCompletionSource.TrySetResult(rewardedAd);
        }

        public override void OnAdFailedToLoad(LoadAdError addError)
        {
            _loadedCompletionSource.TrySetException(new AdLoadException(addError.Message));
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
            _dismissedCompletionSource.TrySetException(new AdException(adError.Message));
        }
    }
    private class MyOnUserEarnedRewardListener : Java.Lang.Object, IOnUserEarnedRewardListener
    {
        private readonly TaskCompletionSource<IRewardItem> _earnedRewardCompletionSource = new();
        public Task<IRewardItem> UserEarnedRewardTask => _earnedRewardCompletionSource.Task;

        public void OnUserEarnedReward(IRewardItem rewardItem)
        {
            _earnedRewardCompletionSource.TrySetResult(rewardItem);
        }
    }
    
    public void Dispose()
    {
    }
}