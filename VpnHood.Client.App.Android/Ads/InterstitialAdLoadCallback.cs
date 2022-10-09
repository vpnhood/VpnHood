#nullable enable
using System;
using Android.Runtime;

namespace VpnHood.Client.App.Android.Ads;

// fix google ad library problem
public abstract class InterstitialAdLoadCallback : global::Android.Gms.Ads.Interstitial.InterstitialAdLoadCallback
{
    private static Delegate? _cbOnAdLoaded;

    // ReSharper disable once UnusedMember.Local
    private static Delegate GetOnAdLoadedHandler()
    {
        return _cbOnAdLoaded ??= JNINativeWrapper.CreateDelegate((Action<IntPtr, IntPtr, IntPtr>)OnAdLoadedNative);
    }

    private static void OnAdLoadedNative(IntPtr env, IntPtr nativeThis, IntPtr nativeP0)
    {
        var interstitialAdLoadCallback = GetObject<InterstitialAdLoadCallback>(env, nativeThis, JniHandleOwnership.DoNotTransfer);
        var interstitialAd = GetObject<global::Android.Gms.Ads.Interstitial.InterstitialAd>(nativeP0, JniHandleOwnership.DoNotTransfer);
        if (interstitialAd != null)
            interstitialAdLoadCallback?.OnAdLoaded(interstitialAd);
    }

    // ReSharper disable once StringLiteralTypo
    [Register("onAdLoaded", "(Lcom/google/android/gms/ads/interstitial/InterstitialAd;)V", "GetOnAdLoadedHandler")]
    public virtual void OnAdLoaded(global::Android.Gms.Ads.Interstitial.InterstitialAd interstitialAd)
    {
    }
}