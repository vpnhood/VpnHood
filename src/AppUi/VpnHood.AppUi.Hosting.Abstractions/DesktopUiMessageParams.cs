using VpnHood.Net.Toolkit.Assets;

namespace VpnHood.AppUi.Hosting.Abstractions;

// What a desktop host hands a UI that cannot run (IDesktopUi.RunMessage): the one message to show,
// and the UI's own content to show it with. Nothing here reaches the app, which may not be running,
// or may have refused whoever asked.
public class DesktopUiMessageParams
{
    public required DesktopUiMessageKind Kind { get; init; }

    // The message's title: the app's name as the head states it (AppInitParams.AppName).
    public required string AppName { get; init; }

    // The message in English: what a UI shows when it has no words of its own for the kind, and,
    // for a failure, why.
    public required string Text { get; init; }

    // The UI's own content, as Run is given it: its words, so a kind it knows is said in the
    // device's language.
    public required IAssetProvider? UiAssetProvider { get; init; }
}
