using Android.Content;
using Android.Gms.Ads;
using Android.Gms.Ads.Initialization;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Droid.Ads.VhAdMob;

public static class AdMobUtil
{
    private static readonly AsyncLock InitLock = new();
    public static bool IsInitialized { get; private set; }
    public static TimeSpan DefaultAdTimeSpan { get; } = TimeSpan.FromMinutes(45);

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
                throw new AdException("Could not find any ad adapter.");

            // at-least one ok
            if (initializationStatus.AdapterStatusMap.Values.Any(value =>
                    value.InitializationState == AdapterStatusState.Ready)) {
                _loadedCompletionSource.TrySetResult();
                return;
            }

            // create an aggregation error from each adapter status
            var errors = initializationStatus.AdapterStatusMap
                .Where(pair => pair.Value.InitializationState != AdapterStatusState.Ready)
                .Select(pair => new AdException($"{pair.Key}: {pair.Value.Description}"))
                .ToList();

            // not success
            _loadedCompletionSource.TrySetException(new AdException($"Could not initialize any ad adapter. {errors}"));
        }
    }
}