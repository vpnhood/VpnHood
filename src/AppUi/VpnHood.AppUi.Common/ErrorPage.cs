namespace VpnHood.AppUi.Common;

// The pages a failure can open in place of the error dialog. Each one is a page a UI must have;
// a UI that cannot draw one has to say so rather than show an empty dialog.

/// <summary>Specifies a page a failure can open in place of the error dialog.</summary>
/// <remarks>Each is a page a UI must have.</remarks>
public enum ErrorPage
{
    /// <summary>The device's private DNS blocks the ad, where custom DNS is sold as premium.</summary>
    PrivateDns
}
