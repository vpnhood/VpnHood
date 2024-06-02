using Android.Gms.Ads;
using Android.Gms.Ads.Rewarded;
using VpnHood.Client.App.Abstractions;
using VpnHood.Client.Device;
using VpnHood.Client.Device.Droid;
using VpnHood.Client.Exceptions;
using VpnHood.Common.Utils;
using Object = Java.Lang.Object;

namespace VpnHood.Client.App.Droid.GooglePlay.Ads;
public class GooglePlayAdService(
    string rewardedAdUnitId)
    : IAppAdService
{
    private MyRewardedAdLoadCallback? _rewardedAdLoadCallback;
    private DateTime _lastLoadRewardedAdTime = DateTime.MinValue;


    public static GooglePlayAdService Create(string rewardedAdUnit)
    {
        var ret = new GooglePlayAdService(rewardedAdUnit);
        return ret;
    }

    public async Task<RewardedAd> LoadRewardedAd(Activity activity, CancellationToken cancellationToken)
    {
        try
        {
            if (_rewardedAdLoadCallback != null && _lastLoadRewardedAdTime.AddHours(1) < DateTime.Now)
                return await _rewardedAdLoadCallback.Task.VhConfigureAwait();

            _rewardedAdLoadCallback = new MyRewardedAdLoadCallback();
            var adRequest = new AdRequest.Builder().Build();
            activity.RunOnUiThread(() => RewardedAd.Load(activity, rewardedAdUnitId, adRequest, _rewardedAdLoadCallback));

            var cancellationTask = new TaskCompletionSource();
            cancellationToken.Register(cancellationTask.SetResult);
            await Task.WhenAny(_rewardedAdLoadCallback.Task, cancellationTask.Task).VhConfigureAwait();
            cancellationToken.ThrowIfCancellationRequested();

            var rewardedAd = await _rewardedAdLoadCallback.Task.VhConfigureAwait();
            _lastLoadRewardedAdTime = DateTime.Now;
            return rewardedAd;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _rewardedAdLoadCallback = null;
            _lastLoadRewardedAdTime = DateTime.MinValue;
            if (ex is AdLoadException) throw;
            throw new AdLoadException("Failed to load ad.", ex);
        }
    }

    public async Task ShowAd(IUiContext uiContext, string customData, CancellationToken cancellationToken)
    {
        var appUiContext = (AndroidUiContext)uiContext;
        var activity = appUiContext.Activity;

        // create ad custom data
        var rewardedAd = await LoadRewardedAd(activity, cancellationToken).VhConfigureAwait();
        if (activity.IsDestroyed)
            throw new AdException("MainActivity has been destroyed before showing the ad.");

        var serverSideVerificationOptions = new ServerSideVerificationOptions.Builder()
        .SetCustomData(customData)
        .Build();

        var fullScreenContentCallback = new MyFullScreenContentCallback();
        var userEarnedRewardListener = new MyOnUserEarnedRewardListener();

        activity.RunOnUiThread(() =>
        {
            rewardedAd.SetServerSideVerificationOptions(serverSideVerificationOptions);
            rewardedAd.FullScreenContentCallback = fullScreenContentCallback;
            rewardedAd.Show(activity, userEarnedRewardListener);
        });

        // wait for earn reward or dismiss
        var cancellationTask = new TaskCompletionSource();
        cancellationToken.Register(cancellationTask.SetResult);
        await Task.WhenAny(fullScreenContentCallback.DismissedTask, userEarnedRewardListener.UserEarnedRewardTask, cancellationTask.Task).VhConfigureAwait();
        cancellationToken.ThrowIfCancellationRequested();

        // check task errors
        if (fullScreenContentCallback.DismissedTask.IsFaulted)
            throw fullScreenContentCallback.DismissedTask.Exception;

        _rewardedAdLoadCallback = null;
    }

    public void Dispose()
    {
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

    private class MyRewardedAdLoadCallback : RewardedAdLoadCallback
    {
        private readonly TaskCompletionSource<RewardedAd> _loadedCompletionSource = new();
        public Task<RewardedAd> Task => _loadedCompletionSource.Task;

        public override void OnAdLoaded(RewardedAd rewardedAd)
        {
            _loadedCompletionSource.TrySetResult(rewardedAd);
        }

        public override void OnAdFailedToLoad(LoadAdError addError)
        {
            _loadedCompletionSource.TrySetException(new AdLoadException(addError.Message));
        }
    }

    private class MyOnUserEarnedRewardListener : Object, IOnUserEarnedRewardListener
    {
        private readonly TaskCompletionSource<IRewardItem> _earnedRewardCompletionSource = new();
        public Task<IRewardItem> UserEarnedRewardTask => _earnedRewardCompletionSource.Task;

        public void OnUserEarnedReward(IRewardItem rewardItem)
        {
            _earnedRewardCompletionSource.TrySetResult(rewardItem);
        }
    }
}
