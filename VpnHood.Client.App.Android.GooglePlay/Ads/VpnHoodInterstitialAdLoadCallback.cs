/*
using Android.Gms.Ads;
using Android.Gms.Ads.Interstitial;
using Object = Java.Lang.Object;

namespace VpnHood.Client.App.Droid.GooglePlay.Ads;

// ReSharper disable All

public class VpnHoodInterstitialAdLoadCallback(Activity activity) : InterstitialAdLoadCallback
{
    private readonly Activity _activity = activity;
    private InterstitialAd? _interstitialAd;

    private class MyFullScreenContentCallback(VpnHoodInterstitialAdLoadCallback adLoadCallback) : FullScreenContentCallback
    {
        private readonly VpnHoodInterstitialAdLoadCallback _adLoadCallback = adLoadCallback;

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

    public override void OnAdLoaded(Object p0)
    {
       // _interstitialAd = (InterstitialAd)p0;
    }

   *//* public void OnAdLoaded(InterstitialAd interstitialAd)
    {
        _interstitialAd = interstitialAd;
        _interstitialAd.FullScreenContentCallback = new MyFullScreenContentCallback(this);
       interstitialAd.Show(_activity);
    }*//*

    public override void OnAdFailedToLoad(LoadAdError addAdError)
    {
        VpnHoodApp.Instance.IsWaitingForAd = false;
    }
}*/