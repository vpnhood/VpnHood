using Android.Content;

namespace VpnHood.Core.Client.Devices.Android.ActivityEvents;

/// <summary>
/// Provides the data of <see cref="IActivityEvent.ActivityResultEvent"/>: the result of an activity this one started.
/// </summary>
public class ActivityResultEventArgs : EventArgs
{
    /// <summary>Gets the code the activity was started with.</summary>
    public int RequestCode { get; init; }

    /// <summary>Gets the activity's result.</summary>
    public Result ResultCode { get; init; }

    /// <summary>Gets the intent the activity returned, or <see langword="null"/>.</summary>
    public Intent? Data { get; init; }
}