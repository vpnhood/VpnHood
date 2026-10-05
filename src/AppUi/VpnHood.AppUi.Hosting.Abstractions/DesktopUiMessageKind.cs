namespace VpnHood.AppUi.Hosting.Abstractions;

// What a UI's one message is about (DesktopUiMessageParams), so a UI with words of its own can say it
// in the person's language.
public enum DesktopUiMessageKind
{
    // Only administrators may use the app on this computer, and whoever runs the UI is not one.
    AdministratorsOnly,

    // The app could not be started or reached; the text says why.
    Failure
}
