using Android.Gms.Ads;
using Android.Gms.Ads.Interstitial;
using Android.Runtime;
using Object = Java.Lang.Object;

namespace VpnHood.Client.App.Droid.GooglePlay.Ads;

public class CustomAdLoadCallback(Activity activity) : VpnHood.Client.App.Droid.GooglePlay.Ads.InterstitialAdLoadCallback
{
    private InterstitialAd? _mInterstitialAd;

    private class FullScreenContentCallback(CustomAdLoadCallback adLoadCallback) : Android.Gms.Ads.FullScreenContentCallback
    {
        private readonly CustomAdLoadCallback _adLoadCallback = adLoadCallback;

        public override void OnAdClicked()
        {
            //VpnHoodApp.Instance.IsWaitingForAd = false;
        }

        public override void OnAdDismissedFullScreenContent()
        {
            base.OnAdDismissedFullScreenContent();
            //VpnHoodApp.Instance.IsWaitingForAd = false;
        }

        public override void OnAdShowedFullScreenContent()
        {
            base.OnAdShowedFullScreenContent();
            //VpnHoodApp.Instance.IsWaitingForAd = false;

        }
    }

    public override void OnAdLoaded(InterstitialAd interstitialAd)
    {
        _mInterstitialAd = interstitialAd;
        _mInterstitialAd.FullScreenContentCallback = new FullScreenContentCallback(this);
        base.OnAdLoaded(interstitialAd);
        try
        {
            _mInterstitialAd.Show(activity);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    public override void OnAdFailedToLoad(LoadAdError loadAdError)
    {
        _mInterstitialAd = null;
        base.OnAdFailedToLoad(loadAdError);
    }
}