﻿using Android.Gms.Ads;
using Android.Gms.Ads.Rewarded;
using VpnHood.Client.App.Abstractions;
using VpnHood.Client.Device;
using VpnHood.Client.Device.Droid;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Utils;
using RewardedAdLoadCallback = VpnHood.Client.App.Droid.Ads.VhAdMob.AdNetworkCallBackOverride.RewardedAdLoadCallback;

namespace VpnHood.Client.App.Droid.Ads.VhAdMob;

public class AdMobRewardedAdService(string adUnitId) : IAppAdService
{
    private RewardedAd? _loadedAd;
    public string NetworkName => "AdMob";
    public AppAdType AdType => AppAdType.RewardedAd;
    public DateTime? AdLoadedTime { get; private set; }

    public static AdMobRewardedAdService Create(string adUnitId)
    {
        var ret = new AdMobRewardedAdService(adUnitId);
        return ret;
    }

    public bool IsCountrySupported(string countryCode)
    {
        // these countries are not supported at all
        return countryCode != "CN" && countryCode != "IR";
    }

    public async Task LoadAd(IUiContext uiContext, CancellationToken cancellationToken)
    {
        var appUiContext = (AndroidUiContext)uiContext;
        var activity = appUiContext.Activity;
        if (activity.IsDestroyed)
            throw new AdLoadException("MainActivity has been destroyed before loading the ad.");

        // reset the last loaded ad
        AdLoadedTime = null;
        _loadedAd = null;

        var adLoadCallback = new MyRewardedAdLoadCallback();
        var adRequest = new AdRequest.Builder().Build();
        activity.RunOnUiThread(() => RewardedAd.Load(activity, adUnitId, adRequest, adLoadCallback));

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
            throw new AdException("MainActivity has been destroyed before showing the ad.");

        if (_loadedAd == null)
            throw new AdException($"The {AdType} has not benn loaded.");

        try
        {
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
        }
        finally
        {
            _loadedAd = null;
            AdLoadedTime = null;
        }
    }

    private class MyRewardedAdLoadCallback : RewardedAdLoadCallback
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