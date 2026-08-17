namespace VpnHood.AppLib;

/// <summary>
/// The premium tier of a build — present only when the product HAS one. Grouped because none of
/// these members mean anything on their own: a null <see cref="AppOptions.Premium" /> is not
/// "premium locked", it is the FULL app — every feature allowed, nothing sold, nothing promoted —
/// and with the members living inside the block, "no premium, but codes are fine" is simply not
/// writable.
/// </summary>
public class AppPremiumOptions
{
    /// <summary>
    /// The features this build sells: present here means "premium profiles only", absent means free
    /// for everyone. Empty is a premium tier that gates nothing — plans still sell time, not features.
    /// </summary>
    public IReadOnlyList<AppFeature> Features { get; init; } = [];

    /// <summary>
    /// Whether this build may take a typed premium code. Default FALSE, because the store that
    /// forbids it refuses the whole app for it: App Review 3.1.1 reads a premium code as a license
    /// key, so a head that never considered the question must not ship a code box by accident.
    /// A per-build capability, never an OS check — a sideloaded iOS build may lawfully keep the box.
    /// </summary>
    public bool IsCodeSupported { get; init; }

    /// <summary>
    /// Whether this build may send a buyer to an outside shop. Default FALSE for the same reason
    /// with more force: the URL arrives from the SERVER — every access token carries its operator's
    /// own — so no policy may be trusted to know which store this build shipped through. Apple
    /// 3.1.1/3.1.3 and Play's Payments policy both forbid steering a buyer out of the app, so a head
    /// sold through either never opts in and the link stays dark whatever the token says.
    /// </summary>
    public bool IsPurchaseUrlSupported { get; init; }

    /// <summary>Drop the profile's access code by itself when the server rejects or expires it.</summary>
    public bool AutoRemoveExpiredAccessCode { get; init; }
}
