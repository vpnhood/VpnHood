namespace VpnHood.AppUi.Common;

// The buttons an error dialog offers beside Close, decided by what went wrong: a retry on the
// automatic location, the premium trial, the ways to replace a refused code, a diagnosis, and the
// report the app is asking for.
public enum ErrorAction
{
    ChangeServerToAuto,
    TryPremium,
    RestorePremium,
    ChangeAccessCode,
    Diagnose,
    OpenReport
}
