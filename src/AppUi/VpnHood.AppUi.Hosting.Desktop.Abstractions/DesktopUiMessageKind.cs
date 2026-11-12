namespace VpnHood.AppUi.Hosting.Desktop.Abstractions;

/// <summary>Specifies what a UI's one message is about, so a UI with words of its own can say it in the person's language.</summary>
public enum DesktopUiMessageKind
{
    /// <summary>Only administrators may use the app on this computer, and whoever runs the UI is not one.</summary>
    AdministratorsOnly,

    /// <summary>The app could not be started or reached, or the launch failed otherwise, as on an access key's file it cannot read; the message's text says why.</summary>
    Failure
}
