using Android.Gms.Ads.AppOpen;
using Android.Runtime;

namespace VpnHood.Client.App.Droid.Ads.VhAdMob.AdNetworkCallBackFix;

public abstract class AppOpenAdLoadCallback : AppOpenAd.AppOpenAdLoadCallback
{
    private static Delegate? _cbOnAdLoaded;

    // ReSharper disable once UnusedMember.Local
    private static Delegate GetOnAdLoadedHandler()
    {
        return _cbOnAdLoaded ??= JNINativeWrapper.CreateDelegate((Action<IntPtr, IntPtr, IntPtr>)OnAdLoadedNative);
    }

    private static void OnAdLoadedNative(IntPtr env, IntPtr nativeThis, IntPtr nativeP0)
    {
        var adLoadCallback = GetObject<AppOpenAdLoadCallback>(env, nativeThis, JniHandleOwnership.DoNotTransfer);
        var appOpenAd = GetObject<AppOpenAd>(nativeP0, JniHandleOwnership.DoNotTransfer);
        if (appOpenAd != null)
            adLoadCallback?.OnAdLoaded(appOpenAd);
    }

    // ReSharper disable StringLiteralTypo
    [Register("onAdLoaded", "(Lcom/google/android/gms/ads/appopen/AppOpenAd;)V", "GetOnAdLoadedHandler")]
    // ReSharper restore StringLiteralTypo
    protected virtual void OnAdLoaded(AppOpenAd appOpenAd)
    {
    }
}