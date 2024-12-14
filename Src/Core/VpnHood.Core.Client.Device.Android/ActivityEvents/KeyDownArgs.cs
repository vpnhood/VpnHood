using Android.Views;

namespace VpnHood.Core.Client.Device.Droid.ActivityEvents;

public class KeyDownArgs
{
    public required Keycode KeyCode { get; init; }
    public required KeyEvent? KeyEvent { get; init; }
    public bool IsHandled { get; set; }
}