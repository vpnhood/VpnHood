namespace VpnHood.Core.Client.Devices.Android.ActivityEvents;

/// <summary>
/// Provides the data of <see cref="IActivityEvent.CreateEvent"/>.
/// </summary>
public class CreateEventArgs
{
    /// <summary>
    /// Gets the state the activity saved before it was last destroyed, or <see langword="null"/>.
    /// </summary>
    public required Bundle? SavedInstanceState { get; init; }
}