namespace VpnHood.AppUi.Common;

// What the sentence and the buttons for a failure depend on besides the failure itself: the
// session's and the profile's standing, and what the app is asking for - which the UI holding the
// app's state reads off it and hands in, so the mapping knows no UI, and a message is the same on
// a device and in a browser.

/// <summary>
/// Represents what the sentence and the buttons for a failure depend on besides the failure: the session's and the
/// profile's standing, and what the app asks for, as the UI holding the app's state reads them.
/// </summary>
public sealed record ErrorContext
{
    /// <summary>Gets a value indicating whether the person asked for a diagnosis, so a diagnosis is not offered again.</summary>
    public bool HasDiagnoseRequested { get; init; }

    /// <summary>Gets a value indicating whether there is a VPN profile.</summary>
    public bool HasVpnProfile { get; init; }

    /// <summary>Gets a value indicating whether the build has a premium tier.</summary>
    public bool IsPremiumSupported { get; init; }

    /// <summary>Gets a value indicating whether the current VPN profile is premium.</summary>
    public bool IsPremiumUser { get; init; }

    /// <summary>Gets a value indicating whether the premium comes from the account's subscription.</summary>
    public bool IsPremiumByAccount { get; init; }

    /// <summary>Gets a value indicating whether the current VPN profile offers a premium trial.</summary>
    public bool CanTryPremium { get; init; }

    /// <summary>Gets a value indicating whether the current VPN profile has a premium code.</summary>
    public bool HasAccessCode { get; init; }

    /// <summary>Gets a value indicating whether a premium code can be typed for the current VPN profile.</summary>
    public bool CanImportAccessCode { get; init; }

    /// <summary>Gets a value indicating whether custom DNS is sold as premium, so a private DNS that blocks the ad has a page of its own.</summary>
    public bool IsCustomDnsPremiumFeature { get; init; }

    /// <summary>Gets a value indicating whether the app asks for a report, so every dialog offers it.</summary>
    public bool PromptForLog { get; init; }
}
