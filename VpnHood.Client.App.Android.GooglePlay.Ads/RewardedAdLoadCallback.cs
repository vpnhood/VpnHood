using Android.Runtime;

namespace VpnHood.Client.App.Droid.GooglePlay.Ads;

public class RewardedAdLoadCallback : global::Android.Gms.Ads.Rewarded.RewardedAdLoadCallback
{
    private static Delegate? _cbOnAdLoaded;

    // ReSharper disable once UnusedMember.Local
    private static Delegate GetOnAdLoadedHandler()
    {
        return _cbOnAdLoaded ??= JNINativeWrapper.CreateDelegate((Action<IntPtr, IntPtr, IntPtr>)OnAdLoadedNative);
    }

    private static void OnAdLoadedNative(IntPtr env, IntPtr nativeThis, IntPtr nativeP0)
    {
        var rewardedAdLoadCallback = GetObject<RewardedAdLoadCallback>(env, nativeThis, JniHandleOwnership.DoNotTransfer);
        var rewardedAd = GetObject<global::Android.Gms.Ads.Rewarded.RewardedAd>(nativeP0, JniHandleOwnership.DoNotTransfer);
        if (rewardedAd != null)
            rewardedAdLoadCallback?.OnAdLoaded(rewardedAd);
    }

    // ReSharper disable once StringLiteralTypo
    [Register("onAdLoaded", "(Lcom/google/android/gms/ads/rewarded/RewardedAd;)V", "GetOnAdLoadedHandler")]
    public virtual void OnAdLoaded(global::Android.Gms.Ads.Rewarded.RewardedAd rewardedAd)
    {
    }
}