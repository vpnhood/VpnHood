namespace VpnHood.AppLib.Ads.AdMob.Android;

// Google's test ad units, from AdMob's documentation: any app may load them; they show ads marked
// "Test Ad", earn nothing and touch no one's account. A rewarded one grants no reward, since AdMob
// confirms a reward only to the server its unit names. The providers never fall back to them: a head
// that wants test ads passes one.
public static class AdMobTestAdUnitIds
{
    public const string AppOpen = "ca-app-pub-3940256099942544/9257395921";
    public const string Interstitial = "ca-app-pub-3940256099942544/1033173712";
    public const string Rewarded = "ca-app-pub-3940256099942544/5224354917";
}
