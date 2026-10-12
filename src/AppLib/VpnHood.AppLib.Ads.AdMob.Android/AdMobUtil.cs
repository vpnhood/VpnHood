using Google.Android.Gms.Ads;
using Google.Android.Gms.Ads.Initialization;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.AppLib.Abstractions.Ads.AdExceptions;

namespace VpnHood.AppLib.Ads.AdMob.Android;

public static class AdMobUtil
{
    private static readonly AsyncLock InitLock = new();
    public static bool IsInitialized { get; private set; }
    public static TimeSpan DefaultAdTimeSpan { get; } = TimeSpan.FromMinutes(20);

    // Every ad comes through here, so none is asked for before the person's consent where one is due.
    public static async Task Initialize(Activity activity, CancellationToken cancellationToken)
    {
        await GatherConsent(activity, cancellationToken).Vhc();

        using var lockAsync = await InitLock.LockAsync(cancellationToken).Vhc();
        if (IsInitialized)
            return;

        var initializeListener = new MyOnInitializationCompleteListener();
        MobileAds.Initialize(activity, initializeListener);
        await initializeListener.Task;
        IsInitialized = true;
    }

    private static async Task GatherConsent(Activity activity, CancellationToken cancellationToken)
    {
        try {
            await AdMobConsent.Gather(activity, cancellationToken).Vhc();
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception ex) {
            // Google's rule: an answer from an earlier run still counts when this one cannot get one
            VhLogger.Instance.LogWarning(ex, "Could not gather the ad consent.");
        }

        if (!AdMobConsent.CanRequestAds)
            throw new LoadAdException("AdMob may not ask for an ad before the person answers its consent form.");
    }

    private class MyOnInitializationCompleteListener : Java.Lang.Object, IOnInitializationCompleteListener
    {
        private readonly TaskCompletionSource _loadedCompletionSource = new();
        public Task Task => _loadedCompletionSource.Task;

        public void OnInitializationComplete(IInitializationStatus initializationStatus)
        {
            // no adapter
            if (initializationStatus.AdapterStatusMap.Keys.Count == 0)
                _loadedCompletionSource.TrySetException(new AdException("Could not find any ad adapter."));

            // at-least one ok
            if (initializationStatus.AdapterStatusMap.Values.Any(value =>
                    value.InitializationState.Equals(AdapterStatusState.Ready))) {
                _loadedCompletionSource.TrySetResult();
                return;
            }

            // create an aggregation error from each adapter status
            var errors = initializationStatus.AdapterStatusMap
                .Where(pair => !pair.Value.InitializationState.Equals(AdapterStatusState.Ready))
                .Select(pair => $"{pair.Key}: {pair.Value.Description}");

            // not success
            var errorMessage = string.Join(",", errors);
            _loadedCompletionSource.TrySetException(
                new AdException($"Could not initialize any ad adapter. {errorMessage}"));
        }
    }
}