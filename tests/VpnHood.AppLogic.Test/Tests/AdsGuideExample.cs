using VpnHood.AppLogic.Abstractions.Ads;
using VpnHood.AppLogic.App;
using VpnHood.AppLogic.App.Services.Ads;

namespace VpnHood.AppLogic.Test.Tests;

// docs/ads.md, the guide "How Ads Work in a VpnHood App", is a copy of the Snippet region here, made by
// pub/lib/Update-Snippets.ps1; the NuGet publish fails when a copy differs. Edit the region, then run the script.
// Compiled with the tests and never run: the networks' packages are Android's, so their providers come in as
// arguments.
internal static class AdsGuideExample
{
    internal static void AddAdNetworks(AppOptions options, IAdProvider adMobInterstitial, IAdProvider adMobRewarded,
        IAdProvider chartboost, IAdProvider inMobi)
    {
        #region Snippet:AdsInTheHead
        // in the head's AppOptionsFactory, with the providers made by their packages' Create;
        // the networks take turns in this order
        options.AdProviderItems = [
            new AppAdProviderItem { AdProvider = adMobInterstitial },
            new AppAdProviderItem { AdProvider = chartboost },
            // only in the countries listed, here an example's
            new AppAdProviderItem { AdProvider = inMobi, IncludeCountryCodes = ["BR", "MX", "AR"] },
            new AppAdProviderItem { AdProvider = adMobRewarded },
            // the app's own ad, which the UI shows: it always comes after the networks
            new AppAdProviderItem { AdProvider = new InternalInAdProvider() }
        ];
        #endregion
    }
}
