namespace VpnHood.AppUi.Services;

// The buttons an error dialog offers beside Close, decided by what went wrong (the web UI's
// ShowErrorActions): a diagnosis, a retry on the automatic location, a premium trial, and for a
// refused code, the ways to replace it.
public sealed record ErrorActions
{
    public bool ShowDiagnose { get; init; }
    public bool ShowChangeServerToAuto { get; init; }
    public bool ShowAccessCodeActions { get; init; }
    public bool ShowChangeAccessCode { get; init; }
    public bool IsPrivateDnsError { get; init; }
    public bool ShowTryPremium { get; init; }
}
