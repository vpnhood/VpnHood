namespace VpnHood.AppLib.Ads.AdMob.Android;

/// <summary>Provides Google's test ad units, from AdMob's documentation.</summary>
/// <remarks>
/// Any app may load them: they show ads marked "Test Ad", earn nothing and touch no one's account. The providers never
/// fall back to them: a head that wants test ads passes one.
/// </remarks>
public static class AdMobTestAdUnitIds
{
    /// <summary>The test ad unit of app-open ads.</summary>
    public const string AppOpen = "ca-app-pub-3940256099942544/9257395921";

    /// <summary>The test ad unit of interstitial ads.</summary>
    public const string Interstitial = "ca-app-pub-3940256099942544/1033173712";

    /// <summary>The test ad unit of rewarded ads.</summary>
    public const string Rewarded = "ca-app-pub-3940256099942544/5224354917";
}
