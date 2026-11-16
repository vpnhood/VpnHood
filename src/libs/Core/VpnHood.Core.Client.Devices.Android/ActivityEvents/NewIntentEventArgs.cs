using Android.Content;

namespace VpnHood.Core.Client.Devices.Android.ActivityEvents;

/// <summary>
/// Provides the data of <see cref="IActivityEvent.NewIntentEvent"/>.
/// </summary>
public class NewIntentEventArgs
{
    /// <summary>Gets the new intent, or <see langword="null"/>.</summary>
    public required Intent? Intent { get; init; }
}