using Google.Android.Gms.Ads;
using Google.Android.Gms.Ads.Interstitial;
using VpnHood.AppLib.Abstractions.Ads;
using VpnHood.Core.Client.Devices.Android;
using VpnHood.Core.Client.Devices.Android.Utils;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.AppLib.Abstractions.Ads.AdExceptions;

namespace VpnHood.AppLib.Ads.AdMob.Android;

/// <summary>Provides AdMob's interstitial ads.</summary>
/// <param name="adUnitId">The AdMob ad unit's ID; <see cref="AdMobTestAdUnitIds"/> has Google's test units.</param>
public class AdMobInterstitialAdProvider(string adUnitId) : IAdProvider
{
    private InterstitialAd? _loadedAd;
    /// <inheritdoc/>
    /// <remarks>It is <c>AdMob</c>.</remarks>
    public string NetworkName => "AdMob";
    /// <inheritdoc/>
    public AdType AdType => AdType.InterstitialAd;
    /// <inheritdoc/>
    public DateTime? AdLoadedTime { get; private set; }
    /// <inheritdoc/>
    /// <remarks>It is 20 minutes.</remarks>
    public TimeSpan AdLifeSpan => AdMobUtil.DefaultAdTimeSpan;

    /// <summary>Creates a provider of AdMob's interstitial ads.</summary>
    /// <param name="adUnitId">The AdMob ad unit's ID.</param>
    /// <returns>The provider.</returns>
    public static AdMobInterstitialAdProvider Create(string adUnitId)
    {
        var ret = new AdMobInterstitialAdProvider(adUnitId);
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

        var adLoadCallback = new MyInterstitialAdLoadCallback();
        var adRequest = new AdRequest.Builder().Build();

        // AdMob load ad must call from main thread
        await AndroidUtils.RunOnUiThread(activity,
                () => InterstitialAd.Load(activity, adUnitId, adRequest, adLoadCallback))
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);


        _loadedAd = await adLoadCallback.Task
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        AdLoadedTime = DateTime.UtcNow;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It shows the ad over the activity, and completes once the person has closed it; <paramref name="customData"/> is
    /// not used.
    /// A show that starts drops the loaded ad however it ends; one that fails its checks keeps it. A cancel ends the
    /// wait, not an ad already shown.
    /// </remarks>
    /// <exception cref="ShowAdException">The activity is destroyed, no ad is loaded, or AdMob could not show it.</exception>
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
            await AndroidUtils
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

    private class MyInterstitialAdLoadCallback : InterstitialAdLoadCallback
    {
        private readonly TaskCompletionSource<InterstitialAd> _loadedCompletionSource = new();
        public Task<InterstitialAd> Task => _loadedCompletionSource.Task;

        public override void OnAdLoaded(InterstitialAd interstitialAd)
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

    /// <inheritdoc/>
    /// <remarks>It does nothing: a loaded ad stays.</remarks>
    public void Dispose()
    {
    }
}