using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.Views;

namespace VpnHood.Client.Device.Droid.Utils;

public interface IActivityEvent
{
    event EventHandler<ActivityResultEventArgs> ActivityResultEvent;
    event EventHandler<CreateEventArgs> CreateEvent;
    event EventHandler<NewIntentEventArgs> NewIntentEvent;
    event EventHandler<RequestPermissionsResultArgs> RequestPermissionsResultEvent;
    event EventHandler<KeyDownArgs> KeyDownEvent;
    event EventHandler DestroyEvent;
    public event EventHandler<Configuration>? ConfigurationChangedEvent;
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

