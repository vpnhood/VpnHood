namespace VpnHood.AppLib.Api.App;

// The pages this build links to, from the product's appsettings ("Links"; see AppOptions.Links). Null
// means the build has no such page: the UI hides its link rather than guess an address.
public record AppLinks
{
    // The legal documents, linked from the paywall, Settings > Privacy and the first-run screen where
    // that is shown; the privacy policy from the menu too.
    public Uri? PrivacyPolicy { get; init; }
    public Uri? TermsOfUse { get; init; }

    // The menu's: the product's website, its "What's new" page, where to send feedback, the guide to
    // a server of one's own (shown only where keys can be added, AppFeatures.IsAddAccessKeySupported)
    // and its social pages.
    public Uri? Website { get; init; }
    public Uri? WhatsNew { get; init; }
    public Uri? Feedback { get; init; }
    public Uri? PersonalServer { get; init; }
    public Uri? LinkedIn { get; init; }
    public Uri? Instagram { get; init; }
    public Uri? X { get; init; }
}
