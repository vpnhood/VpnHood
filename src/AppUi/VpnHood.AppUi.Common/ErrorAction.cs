namespace VpnHood.AppUi.Common;

// The buttons an error dialog offers beside Close, decided by what went wrong: a retry on the
// automatic location, the premium trial, the ways to replace a refused code, a diagnosis, and the
// report the app is asking for.

/// <summary>Specifies a button an error dialog offers beside Close.</summary>
public enum ErrorAction
{
    /// <summary>Try again on the automatic location.</summary>
    ChangeServerToAuto,

    /// <summary>Try premium.</summary>
    TryPremium,

    /// <summary>Restore premium, from the account or the store.</summary>
    RestorePremium,

    /// <summary>Change the premium code.</summary>
    ChangeAccessCode,

    /// <summary>Diagnose the connection.</summary>
    Diagnose,

    /// <summary>Open the app's log, for the report the app asks for.</summary>
    OpenReport
}
