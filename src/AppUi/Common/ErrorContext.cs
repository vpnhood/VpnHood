namespace VpnHood.AppUi.Common;

// What the sentence and the buttons for a failure depend on besides the failure itself: the
// session's and the profile's standing, and what the app is asking for - which the UI holding the
// app's state reads off it and hands in, so the mapping knows no UI, and a message is the same on
// a device and in a browser.
public sealed record ErrorContext
{
    public bool HasDiagnoseRequested { get; init; }
    public bool HasClientProfile { get; init; }
    public bool IsPremiumSupported { get; init; }
    public bool IsPremiumUser { get; init; }
    public bool IsPremiumByAccount { get; init; }
    public bool CanTryPremium { get; init; }
    public bool HasAccessCode { get; init; }
    public bool CanImportAccessCode { get; init; }

    // custom DNS is sold as premium, so a private DNS that blocks the ad has a page of its own
    public bool IsCustomDnsPremiumFeature { get; init; }

    // the app is asking for a report, so every dialog offers it
    public bool PromptForLog { get; init; }
}
