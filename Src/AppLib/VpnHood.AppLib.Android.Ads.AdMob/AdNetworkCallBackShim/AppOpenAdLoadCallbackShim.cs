using Android.Runtime;
using Google.Android.Gms.Ads.AppOpen;
// ReSharper disable UnusedMember.Local

namespace VpnHood.AppLib.Droid.Ads.VhAdMob.AdNetworkCallBackShim;

public abstract class AppOpenAdLoadCallbackShim : AppOpenAd.AppOpenAdLoadCallback
{
    private static Delegate? _cbOnAdLoaded;

    private static Delegate GetOnAdLoadedHandler()
    {
        return _cbOnAdLoaded ??= JNINativeWrapper.CreateDelegate((Action<IntPtr, IntPtr, IntPtr>)OnAdLoadedNative);
    }

    private static void OnAdLoadedNative(IntPtr env, IntPtr nativeThis, IntPtr nativeP0)
    {
        var adLoadCallback = GetObject<AppOpenAdLoadCallbackShim>(env, nativeThis, JniHandleOwnership.DoNotTransfer);
        var appOpenAd = GetObject<AppOpenAd>(nativeP0, JniHandleOwnership.DoNotTransfer);
        if (appOpenAd != null)
            adLoadCallback?.OnAdLoaded(appOpenAd);
    }

    private static void n_OnAdLoaded(IntPtr jnienv, IntPtr nativeThis, IntPtr nativeP0)
    {
        OnAdLoadedNative(jnienv, nativeThis, nativeP0);
    }

    // ReSharper disable StringLiteralTypo
    [Register("onAdLoaded", "(Lcom/google/android/gms/ads/appopen/AppOpenAd;)V", "GetOnAdLoadedHandler")]
    // ReSharper restore StringLiteralTypo
    protected virtual void OnAdLoaded(AppOpenAd appOpenAd)
    {
    }
}