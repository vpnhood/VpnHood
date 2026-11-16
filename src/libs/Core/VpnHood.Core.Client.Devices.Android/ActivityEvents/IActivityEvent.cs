using Android.Content.Res;

namespace VpnHood.Core.Client.Devices.Android.ActivityEvents;

/// <summary>
/// Defines an Android activity whose lifecycle calls and results are raised as events.
/// </summary>
public interface IActivityEvent
{
    /// <summary>Occurs when an activity this one started returns its result.</summary>
    event EventHandler<ActivityResultEventArgs> ActivityResultEvent;

    /// <summary>Occurs when the activity is created.</summary>
    event EventHandler<CreateEventArgs> CreateEvent;

    /// <summary>Occurs when the activity, already running, is started again with a new intent.</summary>
    event EventHandler<NewIntentEventArgs> NewIntentEvent;

    /// <summary>Occurs when the person has answered a request for permissions.</summary>
    event EventHandler<RequestPermissionsResultArgs> RequestPermissionsResultEvent;

    /// <summary>Occurs when a key is pressed, before the activity handles it.</summary>
    event EventHandler<KeyDownArgs> KeyDownEvent;

    /// <summary>Occurs when the activity is paused.</summary>
    event EventHandler? PauseEvent;

    /// <summary>Occurs when the activity is resumed.</summary>
    event EventHandler? ResumeEvent;

    /// <summary>Occurs when the activity is about to be destroyed.</summary>
    event EventHandler DestroyEvent;

    /// <summary>Occurs when the device's configuration has changed, with the new one.</summary>
    event EventHandler<Configuration>? ConfigurationChangedEvent;

    /// <summary>Gets the activity.</summary>
    Activity Activity { get; }
}