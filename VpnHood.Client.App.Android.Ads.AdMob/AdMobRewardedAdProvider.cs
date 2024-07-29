using Android.Gms.Ads;
using Android.Gms.Ads.Rewarded;
using VpnHood.Client.App.Abstractions;
using VpnHood.Client.App.Exceptions;
using VpnHood.Client.Device;
using VpnHood.Client.Device.Droid;
using VpnHood.Common.Exceptions;

namespace VpnHood.Client.App.Droid.Ads.VhAdMob;

public class AdMobRewardedAdProvider(string adUnitId) : IAppAdProvider
{
    private RewardedAd? _loadedAd;
    public string NetworkName => "AdMob";
    public AppAdType AdType => AppAdType.RewardedAd;
    public DateTime? AdLoadedTime { get; private set; }
    public TimeSpan AdLifeSpan => AdMobUtil.DefaultAdTimeSpan;

    public static AdMobRewardedAdProvider Create(string adUnitId)
    {
        var ret = new AdMobRewardedAdProvider(adUnitId);
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

        var adLoadCallback = new MyRewardedAdLoadCallback();
        var adRequest = new AdRequest.Builder().Build();
        activity.RunOnUiThread(() => RewardedAd.Load(activity, adUnitId, adRequest, adLoadCallback));

        var cancellationTask = new TaskCompletionSource();
        cancellationToken.Register(cancellationTask.SetResult);
        await Task.WhenAny(adLoadCallback.Task, cancellationTask.Task).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        _loadedAd = await adLoadCallback.Task.ConfigureAwait(false);
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
            // create ad custom data
            var verificationOptions = new ServerSideVerificationOptions.Builder()
                .SetCustomData(customData ?? "")
                .Build();

            var fullScreenContentCallback = new MyFullScreenContentCallback();
            var userEarnedRewardListener = new MyOnUserEarnedRewardListener();

            activity.RunOnUiThread(() => {
                _loadedAd.SetServerSideVerificationOptions(verificationOptions);
                _loadedAd.FullScreenContentCallback = fullScreenContentCallback;
                _loadedAd.Show(activity, userEarnedRewardListener);
            });

            // wait for earn reward or dismiss
            var cancellationTask = new TaskCompletionSource();
            cancellationToken.Register(cancellationTask.SetResult);
            await Task.WhenAny(fullScreenContentCallback.DismissedTask, userEarnedRewardListener.UserEarnedRewardTask,
                cancellationTask.Task).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            // check task errors
            if (fullScreenContentCallback.DismissedTask.IsFaulted)
                throw fullScreenContentCallback.DismissedTask.Exception;
        }
        finally {
            _loadedAd = null;
            AdLoadedTime = null;
        }
    }

    private class MyRewardedAdLoadCallback : AdNetworkCallBackFix.RewardedAdLoadCallback
    {
        private readonly TaskCompletionSource<RewardedAd> _loadedCompletionSource = new();
        public Task<RewardedAd> Task => _loadedCompletionSource.Task;

        protected override void OnAdLoaded(RewardedAd rewardedAd)
        {
            _loadedCompletionSource.TrySetResult(rewardedAd);
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