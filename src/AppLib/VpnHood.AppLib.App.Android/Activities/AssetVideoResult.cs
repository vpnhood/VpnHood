namespace VpnHood.AppLib.App.Android.Activities;

/// <summary>Specifies how <see cref="AssetVideoActivity"/> ended.</summary>
public enum AssetVideoResult
{
    /// <summary>The screen ended with no answer of its own, as where the system finished it.</summary>
    Left,

    /// <summary>
    /// The person closed it by the close mark, Close or Back, once the video had run or earlier where allowed; or the screen
    /// closed itself when the person left it after the end.
    /// </summary>
    Closed,

    /// <summary>The person chose the action button, once the video had run.</summary>
    Action
}
