using Android.Content;
using Android.Gms.Ads;
using Android.Gms.Ads.Initialization;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Utils;

namespace VpnHood.Client.App.Droid.Ads.VhAdMob;

public class AdMobUtil
{
    private static readonly AsyncLock InitLock = new();
    public static bool IsInitialized { get; private set; }
    public static TimeSpan DefaultAdTimeSpan { get; } = TimeSpan.FromMinutes(45);
    public static async Task Initialize(Context context, CancellationToken cancellationToken)
    {
        using var lockAsync = await InitLock.LockAsync(cancellationToken);
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
                    value.InitializationState == AdapterStatusState.Ready))
            {
                _loadedCompletionSource.TrySetResult();
                return;
            }

            // not success
            _loadedCompletionSource.TrySetException(new AdException("Could not initialize any ad adapter."));
        }
    }
}