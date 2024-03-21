
using Android.Gms.Ads;
using Android.Gms.Ads.Interstitial;
using Android.Runtime;

namespace VpnHood.Client.App.Droid.GooglePlay.Ads;

// fix google ad library problem
public class InterstitialAdLoadCallback2(Activity activity) : InterstitialAdLoadCallback
{
    private static Delegate? _cbOnAdLoaded;
    private InterstitialAd? _mInterstitialAd;

    // ReSharper disable once UnusedMember.Local
    private static Delegate GetOnAdLoadedHandler()
    {
        return _cbOnAdLoaded ??= JNINativeWrapper.CreateDelegate((Action<IntPtr, IntPtr, IntPtr>)OnAdLoadedNative);
    }

    private static void OnAdLoadedNative(IntPtr env, IntPtr nativeThis, IntPtr nativeP0)
    {
        var interstitialAdLoadCallback = GetObject<InterstitialAdLoadCallback2>(env, nativeThis, JniHandleOwnership.DoNotTransfer);
        var interstitialAd = GetObject<InterstitialAd>(nativeP0, JniHandleOwnership.DoNotTransfer);
        if (interstitialAd != null)
            interstitialAdLoadCallback?.OnAdLoaded(interstitialAd);
    }

    // ReSharper disable once StringLiteralTypo
    [Register("onAdLoaded", "(Lcom/google/android/gms/ads/interstitial/InterstitialAd;)V", "GetOnAdLoadedHandler")]
    public virtual void OnAdLoaded(InterstitialAd interstitialAd)
    {
        _mInterstitialAd = interstitialAd;
        base.OnAdLoaded(interstitialAd);
    }

    public override void OnAdFailedToLoad(LoadAdError loadAdError)
    {
        _mInterstitialAd = null;
        base.OnAdFailedToLoad(loadAdError);
    }
}
