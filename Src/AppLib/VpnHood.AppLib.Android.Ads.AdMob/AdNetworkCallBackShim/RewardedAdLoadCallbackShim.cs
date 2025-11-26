using Android.Runtime;
using Google.Android.Gms.Ads.Rewarded;

// ReSharper disable UnusedMember.Local

namespace VpnHood.AppLib.Droid.Ads.VhAdMob.AdNetworkCallBackShim;

public abstract class RewardedAdLoadCallbackShim : RewardedAdLoadCallback
{
    private static Delegate? _cbOnAdLoaded;

    // ReSharper disable once UnusedMember.Local
    private static Delegate GetOnAdLoadedHandler()
    {
        return _cbOnAdLoaded ??= JNINativeWrapper.CreateDelegate((Action<IntPtr, IntPtr, IntPtr>)OnAdLoadedNative);
    }

    // Add the native callback method explicitly
    private static void n_OnAdLoaded(IntPtr jnienv, IntPtr nativeThis, IntPtr nativeP0)
    {
        OnAdLoadedNative(jnienv, nativeThis, nativeP0);
    }

    private static void OnAdLoadedNative(IntPtr env, IntPtr nativeThis, IntPtr nativeP0)
    {
        var rewardedAdLoadCallback =
            GetObject<RewardedAdLoadCallbackShim>(env, nativeThis, JniHandleOwnership.DoNotTransfer);
        var rewardedAd = GetObject<RewardedAd>(nativeP0, JniHandleOwnership.DoNotTransfer);
        if (rewardedAd != null)
            rewardedAdLoadCallback?.OnAdLoaded(rewardedAd);
    }

    // ReSharper disable once StringLiteralTypo
    [Register("onAdLoaded", "(Lcom/google/android/gms/ads/rewarded/RewardedAd;)V", "GetOnAdLoadedHandler")]
    protected virtual void OnAdLoaded(RewardedAd rewardedAd)
    {
    }
}