using Android.Content;
using Android.Content.PM;
using Android.Views;

namespace VpnHood.Client.Device.Droid.Utils;

public interface IActivityEvent
{
    event EventHandler<ActivityResultEventArgs> OnActivityResultEvent;
    event EventHandler<CreateEventArgs> OnCreateEvent;
    event EventHandler<NewIntentEventArgs> OnNewIntentEvent;
    event EventHandler<RequestPermissionsResultArgs> OnRequestPermissionsResultEvent;
    event EventHandler<KeyDownArgs> OnKeyDownEvent;
    event EventHandler OnDestroyEvent;
    Activity Activity { get; }
}

public class KeyDownArgs
{
    public required Keycode KeyCode { get; init; }
    public required KeyEvent? KeyEvent { get; init; }
    public bool IsHandled { get; set; }
}

public class RequestPermissionsResultArgs
{
    public required int RequestCode { get; init; }
    public required string[] Permissions { get; init; }
    public required Permission[] GrantResults { get; init; }
}

public class NewIntentEventArgs
{
    public required Intent? Intent { get; init; }
}

public class CreateEventArgs
{
    public required Bundle? SavedInstanceState { get; init; }
}

