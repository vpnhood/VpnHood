namespace VpnHood.AppLib.App.Android.Activities;

// How AssetVideoActivity ended.
public enum AssetVideoResult
{
    // gone before the end with no choice of the person's: the system finished it
    Left,

    // closed once the video had run - by the close mark, Close or Back - or earlier where allowed
    Closed,

    // the action button, once the video had run
    Action
}
