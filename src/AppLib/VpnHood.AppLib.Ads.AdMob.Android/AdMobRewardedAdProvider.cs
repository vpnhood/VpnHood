using Google.Android.Gms.Ads;
using Google.Android.Gms.Ads.Rewarded;
using VpnHood.AppLib.Abstractions.Ads;
using VpnHood.Core.Client.Devices.Android;
using VpnHood.Core.Client.Devices.Android.Utils;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.AppLib.Abstractions.Ads.AdExceptions;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppLib.Ads.AdMob.Android;

/// <summary>Provides AdMob's rewarded ads.</summary>
/// <param name="adUnitId">The AdMob ad unit's ID; <see cref="AdMobTestAdUnitIds"/> has Google's test units.</param>
public class AdMobRewardedAdProvider(string adUnitId) : IAdProvider
{
    private RewardedAd? _loadedAd;
    /// <inheritdoc/>
    /// <remarks>It is <c>AdMob</c>.</remarks>
    public string NetworkName => "AdMob";
    /// <inheritdoc/>
    public AdType AdType => AdType.RewardedAd;
    /// <inheritdoc/>
    public DateTime? AdLoadedTime { get; private set; }
    /// <inheritdoc/>
    /// <remarks>It is 20 minutes.</remarks>
    public TimeSpan AdLifeSpan => AdMobUtil.DefaultAdTimeSpan;

    /// <summary>
    /// Gets or sets how long, after the ad has closed, the provider waits for AdMob's SDK to report the reward earned.
    /// The default is 3 seconds.
    /// </summary>
    /// <remarks>
    /// It counts whole milliseconds, a fraction dropped toward zero: −1 ms (<see cref="Timeout.InfiniteTimeSpan"/>)
    /// waits without a limit, and a value outside −1 ms to 4,294,967,294 ms makes <see cref="ShowAd"/> throw
    /// <see cref="ArgumentOutOfRangeException"/> once the ad has closed.
    /// </remarks>
    public TimeSpan AdEarnedTimeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Creates a provider of AdMob's rewarded ads.</summary>
    /// <param name="adUnitId">The AdMob ad unit's ID.</param>
    /// <returns>The provider.</returns>
    public static AdMobRewardedAdProvider Create(string adUnitId)
    {
        var ret = new AdMobRewardedAdProvider(adUnitId);
        return ret;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It gathers the person's consent where one is due and starts AdMob's SDK, then loads the ad on the UI thread. A
    /// load that gets that far drops the ad loaded before; one that fails earlier, as for a destroyed activity, keeps it.
    /// </remarks>
    /// <exception cref="LoadAdException">
    /// The activity is destroyed, the consent AdMob needs is missing, or AdMob could not load an ad.
    /// </exception>
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

        var adLoadCallback = new MyRewardedAdLoadCallback();
        var adRequest = new AdRequest.Builder().Build();
        await AndroidUtils.RunOnUiThread(activity, () => RewardedAd.Load(activity, adUnitId, adRequest, adLoadCallback))
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        _loadedAd = await adLoadCallback.Task
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        AdLoadedTime = DateTime.UtcNow;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It shows the ad over the activity, with <paramref name="customData"/> for AdMob's server-side verification of the
    /// reward, which it does not wait for, and completes once the person has closed it and AdMob's SDK has reported the
    /// reward earned, which it waits <see cref="AdEarnedTimeout"/> for.
    /// A show that starts drops the loaded ad however it ends; one that fails its checks keeps it. A cancel ends the
    /// wait, not an ad already shown.
    /// </remarks>
    /// <exception cref="ShowAdException">The activity is destroyed, no ad is loaded, or AdMob could not show it.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="AdEarnedTimeout"/> is out of its range, found once the ad has closed.
    /// </exception>
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
            // create ad custom data
            var verificationOptions = new ServerSideVerificationOptions.Builder()
                .SetCustomData(customData ?? "")
                .Build();

            var fullScreenContentCallback = new AdMobFullScreenContentCallback();
            var userEarnedRewardListener = new MyOnUserEarnedRewardListener();

            await AndroidUtils
                .RunOnUiThread(activity, () => {
                    _loadedAd.SetServerSideVerificationOptions(verificationOptions);
                    _loadedAd.FullScreenContentCallback = fullScreenContentCallback;
                    _loadedAd.Show(activity, userEarnedRewardListener);
                })
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);


            var result = await fullScreenContentCallback.DismissedTask
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            // if user reward not received in given time, throw exception
            try {
                await userEarnedRewardListener.UserEarnedRewardTask.WaitAsync(AdEarnedTimeout, cancellationToken).Vhc();
            }
            catch (TimeoutException) {
                throw new RewardNotEarnedException(
                    $"Could not receive user reward in {AdEarnedTimeout.TotalSeconds} seconds after closing the ad.");
            }

            return result;
        }
        finally {
            _loadedAd = null;
            AdLoadedTime = null;
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
            _loadedCompletionSource.TrySetException(
                addError.Message.Contains("No fill.", StringComparison.OrdinalIgnoreCase)
                    ? new NoFillAdException(addError.Message)
                    : new LoadAdException(addError.Message));
        }
    }

    private class MyOnUserEarnedRewardListener : Java.Lang.Object, IOnUserEarnedRewardListener
    {
        private readonly TaskCompletionSource<IRewardItem> _earnedRewardCompletionSource = new();

        // ReSharper disable once UnusedMember.Local
        public Task<IRewardItem> UserEarnedRewardTask => _earnedRewardCompletionSource.Task;

        public void OnUserEarnedReward(IRewardItem rewardItem)
        {
            _earnedRewardCompletionSource.TrySetResult(rewardItem);
        }
    }

    /// <inheritdoc/>
    /// <remarks>It does nothing: a loaded ad stays.</remarks>
    public void Dispose()
    {
    }
}