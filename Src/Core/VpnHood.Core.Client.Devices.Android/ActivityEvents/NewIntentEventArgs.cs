using Android.Content;

namespace VpnHood.Core.Client.Devices.Droid.ActivityEvents;

public class NewIntentEventArgs
{
    public required Intent? Intent { get; init; }
}