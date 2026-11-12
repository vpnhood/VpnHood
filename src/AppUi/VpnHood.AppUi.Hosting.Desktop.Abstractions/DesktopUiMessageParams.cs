using VpnHood.Net.Toolkit.Assets;

namespace VpnHood.AppUi.Hosting.Desktop.Abstractions;

/// <summary>Represents what a desktop host hands a UI's <see cref="IDesktopUi.RunMessage"/>: the one message to show, and the UI's own content to show it with.</summary>
/// <remarks>Nothing here reaches the app, which may not be running, or may have refused whoever asked.</remarks>
public class DesktopUiMessageParams
{
    /// <summary>Gets what the message is about.</summary>
    public required DesktopUiMessageKind Kind { get; init; }

    /// <summary>Gets the message's title: the app's name, as the head states it.</summary>
    public required string AppName { get; init; }

    /// <summary>Gets the host's text of the message: what a UI shows where it has no words of its own for the kind, and, for a failure, why.</summary>
    public required string Text { get; init; }

    /// <summary>
    /// Gets the UI's own content, as <see cref="IDesktopUi.Run"/> is given it, so a kind the UI knows is said in the
    /// device's language; or <see langword="null"/>.
    /// </summary>
    public required IAssetProvider? UiAssetProvider { get; init; }
}
