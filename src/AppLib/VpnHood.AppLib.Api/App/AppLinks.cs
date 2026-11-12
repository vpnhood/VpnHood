namespace VpnHood.AppLib.Api.App;

/// <summary>
/// Represents the pages this build links to, from the product's settings; a <see langword="null"/> page is one the build
/// has not, whose link the UI hides.
/// </summary>
public record AppLinks
{
    /// <summary>Gets the privacy policy, linked from the paywall, the privacy settings, the first-run screen and the menu.</summary>
    public Uri? PrivacyPolicy { get; init; }

    /// <summary>Gets the terms of use, linked from the paywall and the first-run screen.</summary>
    public Uri? TermsOfUse { get; init; }

    /// <summary>Gets the product's website, linked from the menu.</summary>
    public Uri? Website { get; init; }

    /// <summary>Gets the page of what is new, linked from the menu.</summary>
    public Uri? WhatsNew { get; init; }

    /// <summary>Gets where to send feedback, linked from the menu.</summary>
    public Uri? Feedback { get; init; }

    /// <summary>Gets the guide to a server of one's own, linked from the menu where access keys can be added (<see cref="AppDescriptor.IsAddAccessKeySupported"/>).</summary>
    public Uri? PersonalServer { get; init; }

    /// <summary>Gets the product's LinkedIn page, linked from the menu.</summary>
    public Uri? LinkedIn { get; init; }

    /// <summary>Gets the product's Instagram page, linked from the menu.</summary>
    public Uri? Instagram { get; init; }

    /// <summary>Gets the product's X page, linked from the menu.</summary>
    public Uri? X { get; init; }
}
