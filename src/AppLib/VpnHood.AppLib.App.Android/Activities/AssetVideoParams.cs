namespace VpnHood.AppLib.App.Android.Activities;

// What AssetVideoActivity shows: the video, the colour around it and the words of its buttons.
public sealed class AssetVideoParams
{
    // a STORED entry of the package's assets, as "ads/film.mp4"
    public required string AssetPath { get; init; }

    // The screen around the video, where its shape is not the screen's. The controls are white, so
    // a dark colour.
    public required System.Drawing.Color BackgroundColor { get; init; }

    public required string CloseText { get; init; }

    // the button beside Close; what it does is the caller's (AssetVideoResult.Action)
    public required string ActionText { get; init; }

    // a Debug build's way past the wait: the close mark and Back work from the start
    public bool CanCloseEarly { get; init; }
}
