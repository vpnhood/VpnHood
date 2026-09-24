namespace VpnHood.App.StoreScreenshots;

// The scene a desktop capture is composited into: the whole picture the store shows, and the width
// the app's window is drawn at inside it. The window's height follows the capture's proportions.
internal sealed class DesktopCanvas
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required double WindowWidth { get; init; }
    public required double TitleBar { get; init; }
}
