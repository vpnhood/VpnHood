using Android.Gms.Ads;
using Android.Gms.Ads.Rewarded;
using Microsoft.Extensions.Logging;
using VpnHood.Client.App.Abstractions;
using VpnHood.Common.Logging;

namespace VpnHood.Client.App.Droid.GooglePlay.Ads;
public class GooglePlayAdService(
    Activity activity,
    string rewardedAdUnitId)
    : IAppAdService
{
    private MyRewardedAdLoadCallback? _rewardedAdLoadCallback;
    private DateTime _lastRewardedAdLoadTime = DateTime.MinValue;


    public static GooglePlayAdService Create(Activity activity, string rewardedAdUnit)
    {
        var ret = new GooglePlayAdService(activity, rewardedAdUnit);
        return ret;
    }

    public async Task<RewardedAd> LoadRewardedAd(CancellationToken cancellationToken)
    {
        try
        {
            if (_rewardedAdLoadCallback != null && _lastRewardedAdLoadTime.AddHours(1) < DateTime.Now)
                return await _rewardedAdLoadCallback.Task;

            _rewardedAdLoadCallback = new MyRewardedAdLoadCallback();
            var adRequest = new AdRequest.Builder().Build();
            activity.RunOnUiThread(() => RewardedAd.Load(activity, rewardedAdUnitId, adRequest, _rewardedAdLoadCallback));

            var cancellationTask = new TaskCompletionSource();
            cancellationToken.Register(cancellationTask.SetResult);
            await Task.WhenAny(_rewardedAdLoadCallback.Task, cancellationTask.Task);
            cancellationToken.ThrowIfCancellationRequested();

            var rewardedAd = await _rewardedAdLoadCallback.Task;
            _lastRewardedAdLoadTime = DateTime.Now;
            return rewardedAd;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            VhLogger.Instance.LogError(ex, "Failed to load ad.");
            _rewardedAdLoadCallback = null;
            _lastRewardedAdLoadTime = DateTime.MinValue;
            throw;
        }
    }

    public async Task<string> ShowAd(CancellationToken cancellationToken)
    {
        // create ad custom data
        var customData = Guid.NewGuid().ToString();
        var rewardedAd = await LoadRewardedAd(cancellationToken);
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
        await Task.WhenAny(fullScreenContentCallback.DismissedTask, userEarnedRewardListener.UserEarnedRewardTask, cancellationTask.Task);
        cancellationToken.ThrowIfCancellationRequested();

        // check task errors
        if (fullScreenContentCallback.DismissedTask.IsFaulted) throw fullScreenContentCallback.DismissedTask.Exception;

        _rewardedAdLoadCallback = null;
        return customData;
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
            _dismissedCompletionSource.TrySetException(new Exception(adError.Message));
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
            _loadedCompletionSource.TrySetException(new Exception(addError.Message));
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
}
