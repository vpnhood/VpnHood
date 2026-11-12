namespace VpnHood.AppLib.App.Android.Activities;

/// <summary>Represents what <see cref="AssetVideoActivity"/> shows: the video, the colour around it, and the words of its buttons.</summary>
public sealed class AssetVideoParams
{
    /// <summary>Gets the video: a stored, not deflated, entry of the package's assets, as <c>ads/film.mp4</c>.</summary>
    public required string AssetPath { get; init; }

    /// <summary>Gets the colour of the screen around the video, where its shape is not the screen's; a dark one, as the controls are white.</summary>
    public required System.Drawing.Color BackgroundColor { get; init; }

    /// <summary>Gets the Close button's words.</summary>
    public required string CloseText { get; init; }

    /// <summary>Gets the words of the button beside Close, whose action is the caller's.</summary>
    public required string ActionText { get; init; }

    /// <summary>Gets a value indicating whether the close mark and Back work from the start, as a debug build's way past the wait.</summary>
    public bool CanCloseEarly { get; init; }
}
