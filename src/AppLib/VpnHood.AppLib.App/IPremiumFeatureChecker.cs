namespace VpnHood.AppLib;

// The premium gate the feature services and settings resolution need. VpnHoodApp implements it, so
// a service can ask about its own feature without seeing anything else the app owns. It answers and
// nothing more: the gate runs on every state poll and every settings resolution, so a warning here
// would fill the log. VpnHoodApp says once, at connect time, which configured features the plan
// dropped.
public interface IPremiumFeatureChecker
{
    // true when the feature may be used with the current plan
    bool IsPremiumFeatureAllowed(AppFeature feature);
}
