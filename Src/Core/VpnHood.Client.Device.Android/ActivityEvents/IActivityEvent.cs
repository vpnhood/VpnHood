using Android.Content.Res;

namespace VpnHood.Client.Device.Droid.ActivityEvents;

public interface IActivityEvent
{
    event EventHandler<ActivityResultEventArgs> ActivityResultEvent;
    event EventHandler<CreateEventArgs> CreateEvent;
    event EventHandler<NewIntentEventArgs> NewIntentEvent;
    event EventHandler<RequestPermissionsResultArgs> RequestPermissionsResultEvent;
    event EventHandler<KeyDownArgs> KeyDownEvent;
    event EventHandler? PauseEvent;
    event EventHandler? ResumeEvent;
    event EventHandler DestroyEvent;
    event EventHandler<Configuration>? ConfigurationChangedEvent;
    Activity Activity { get; }
}