
using VpnHood.AppLib.Api.App;

namespace VpnHood.AppLib.Api.Premium;

/// <summary>Represents a build's premium tier, set only where the product has one.</summary>
/// <remarks>
/// Without it, the app is the full app: every feature allowed, nothing sold or promoted. Its members mean nothing
/// alone, so a build without a premium tier does not offer premium-code entry either.
/// </remarks>
public class AppPremiumOptions
{
    /// <summary>
    /// Gets the features this build sells: one listed is for premium profiles only, and one not listed is free for
    /// everyone. The default is none.
    /// </summary>
    /// <remarks>With none, the tier gates no feature: its plans sell time.</remarks>
    public IReadOnlyList<AppFeature> Features { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether this build takes a typed premium code. The default is <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// A store may refuse the whole app for a code box: Apple's App Review takes a premium code for a license key. It is a
    /// capability of the build, not of the device's system: an iOS build from outside the App Store may keep the box. It
    /// covers only typing a code; whether the code this device holds is shown is the operator's choice
    /// (<see cref="VpnProfiles.VpnProfileInfo.CanViewAccessCode"/>), so a build without the box still shows a buyer the code
    /// their purchase made, for the devices that take it.
    /// </remarks>
    public bool AllowImportAccessCode { get; init; }

    /// <summary>
    /// Gets a value indicating whether this build may send a buyer to a shop outside the app. The default is
    /// <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// The shop's address comes from the server, as each access key carries its operator's, so no policy can know which
    /// store this build came through. Apple's and Google Play's rules forbid sending a buyer out of the app, so a build
    /// sold through either keeps it off, and the link stays hidden whatever the access key says.
    /// </remarks>
    public bool IsPurchaseUrlSupported { get; init; }
}
