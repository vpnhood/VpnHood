#nullable enable
using Android.App;
using Android.Gms.Ads;
using Android.Gms.Ads.Interstitial;

namespace VpnHood.Client.App.Android.Ads;

internal class VpnHoodInterstitialAdLoadCallback : InterstitialAdLoadCallback
{
    private readonly Activity _activity;
    private InterstitialAd? _interstitialAd;

    private class MyFullScreenContentCallback : FullScreenContentCallback
    {
        private readonly VpnHoodInterstitialAdLoadCallback _adLoadCallback;

        public MyFullScreenContentCallback(VpnHoodInterstitialAdLoadCallback adLoadCallback)
        {
            _adLoadCallback = adLoadCallback;
        }

        public override void OnAdClicked()
        {
            VpnHoodApp.Instance.IsWaitingForAd = false;
        }

        public override void OnAdDismissedFullScreenContent()
        {
            base.OnAdDismissedFullScreenContent();
            VpnHoodApp.Instance.IsWaitingForAd = false;
        }

        public override void OnAdShowedFullScreenContent()
        {
            base.OnAdShowedFullScreenContent();
            VpnHoodApp.Instance.IsWaitingForAd = false;

        }
    }

    public VpnHoodInterstitialAdLoadCallback(Activity activity)
    {
        _activity = activity;
    }

    public override void OnAdLoaded(InterstitialAd interstitialAd)
    {
        _interstitialAd = interstitialAd;
        _interstitialAd.FullScreenContentCallback = new MyFullScreenContentCallback(this);
        interstitialAd.Show(_activity);
    }

    public override void OnAdFailedToLoad(LoadAdError addAdError)
    {
        VpnHoodApp.Instance.IsWaitingForAd = false;
    }
}