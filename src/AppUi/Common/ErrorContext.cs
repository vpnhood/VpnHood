namespace VpnHood.AppUi.Common;

// What the sentence for a failure depends on besides the failure itself: the session's and the
// profile's standing, which the UI holding the app's state reads off it and hands in - so the
// mapping knows no UI, and a message is the same on a device and in a browser.
public sealed record ErrorContext
{
    public bool HasDiagnoseRequested { get; init; }
    public bool IsPremiumSupported { get; init; }
    public bool IsPremiumUser { get; init; }
    public bool IsPremiumByAccount { get; init; }
    public bool CanTryPremium { get; init; }
    public bool HasAccessCode { get; init; }
    public bool CanImportAccessCode { get; init; }
}
