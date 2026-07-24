namespace VpnHood.AppLib;

// The minimal premium gate the feature services need. VpnHoodApp implements it, so a service can
// check its own feature without seeing anything else the app owns.
public interface IPremiumFeatureChecker
{
    // true when the feature may be used with the current plan; logs a warning and returns false otherwise
    bool CheckPremiumFeature(AppFeature feature);
}
