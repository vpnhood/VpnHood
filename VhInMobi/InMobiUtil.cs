using Com.Vpnhood.Inmobi.Ads;
using VpnHood.Common.Utils;
using VpnHood.Client.Device.Droid.Utils;

namespace VpnHood.Client.App.Droid.Ads.VhInMobi;

public class InMobiUtil
{
    private static readonly AsyncLock InitLock = new();
    public static bool IsInitialized { get; private set; }

    public static async Task Initialize(Activity activity, string accountId, bool isDebugMode, CancellationToken cancellationToken)
    {
        using var lockAsync = await InitLock.LockAsync(cancellationToken);
        if (IsInitialized)
            return;

        // initialize
        Task? task = null;
        activity.RunOnUiThread(() => {
            task = InMobiAdServiceFactory.InitializeInMobi(activity, accountId, 
                    Java.Lang.Boolean.ValueOf(isDebugMode))!.AsTask();
            Console.WriteLine("test");
        });

        // wait for completion
        if (task != null)
            await task.ConfigureAwait(false);

        IsInitialized = true;
    }
}