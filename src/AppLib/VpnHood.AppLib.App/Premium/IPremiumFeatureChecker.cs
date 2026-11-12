
using VpnHood.AppLib.Api.App;

namespace VpnHood.AppLib.App.Premium;

/// <summary>
/// Defines the premium gate the feature services and the split's resolution ask, which <see cref="VpnHoodApp"/>
/// implements, so a service can ask about its own feature and see nothing else of the app.
/// </summary>
public interface IPremiumFeatureChecker
{
    /// <summary>Returns a value indicating whether a feature may be used on the current plan.</summary>
    /// <param name="feature">The feature.</param>
    /// <returns><see langword="true"/> if the feature may be used; otherwise, <see langword="false"/>.</returns>
    bool IsPremiumFeatureAllowed(AppFeature feature);
}
