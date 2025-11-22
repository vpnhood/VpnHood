using Android.Runtime;
using Google.Android.Gms.Ads.Interstitial;

// ReSharper disable UnusedMember.Local

namespace VpnHood.AppLib.Droid.Ads.VhAdMob.AdNetworkCallBackShim;

public abstract class InterstitialAdLoadCallbackShim : InterstitialAdLoadCallback
{
    private static Delegate? _cbOnAdLoaded;

    private static Delegate GetOnAdLoadedHandler()
    {
        return _cbOnAdLoaded ??= JNINativeWrapper.CreateDelegate((Action<IntPtr, IntPtr, IntPtr>)OnAdLoadedNative);
    }

    private static void OnAdLoadedNative(IntPtr env, IntPtr nativeThis, IntPtr nativeP0)
    {
        var interstitialAdLoadCallback =
            GetObject<InterstitialAdLoadCallbackShim>(env, nativeThis, JniHandleOwnership.DoNotTransfer);
        var interstitialAd = GetObject<InterstitialAd>(nativeP0, JniHandleOwnership.DoNotTransfer);
        if (interstitialAd != null)
            interstitialAdLoadCallback?.OnAdLoaded(interstitialAd);
    }

    private static void n_OnAdLoaded(IntPtr jnienv, IntPtr nativeThis, IntPtr nativeP0)
    {
        OnAdLoadedNative(jnienv, nativeThis, nativeP0);
    }

    // ReSharper disable once StringLiteralTypo
    [Register("onAdLoaded", "(Lcom/google/android/gms/ads/interstitial/InterstitialAd;)V", "GetOnAdLoadedHandler")]
    protected virtual void OnAdLoaded(InterstitialAd interstitialAd)
    {
    }
}