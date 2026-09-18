namespace VpnHood.AppLib.Api.WebHost;

public class WebHostOptions
{
    // The UI this host serves - a zip with index.html at its root - to whoever asks, the app's own
    // web view or a paired device. Which UI is the head's choice: the SPA, or the Avalonia browser build.
    public required ReadOnlyMemory<byte> WebRootZip { get; init; }
}
