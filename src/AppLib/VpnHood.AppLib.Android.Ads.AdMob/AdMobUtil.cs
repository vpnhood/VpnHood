using Android.Content;
using Google.Android.Gms.Ads;
using Google.Android.Gms.Ads.Initialization;
using VpnHood.AppLib.Abstractions.AdExceptions;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Droid.Ads.VhAdMob;

public static class AdMobUtil
{
    private static readonly AsyncLock InitLock = new();
    public static bool IsInitialized { get; private set; }
    public static TimeSpan DefaultAdTimeSpan { get; } = TimeSpan.FromMinutes(20);

    public static async Task Initialize(Context context, CancellationToken cancellationToken)
    {
        using var lockAsync = await InitLock.LockAsync(cancellationToken).Vhc();
        if (IsInitialized)
            return;

        var initializeListener = new MyOnInitializationCompleteListener();
        MobileAds.Initialize(context, initializeListener);
        await initializeListener.Task;
        IsInitialized = true;
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