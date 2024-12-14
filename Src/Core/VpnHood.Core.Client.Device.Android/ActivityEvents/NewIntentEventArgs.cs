using Android.Content;

namespace VpnHood.Core.Client.Device.Droid.ActivityEvents;

public class NewIntentEventArgs
{
    public required Intent? Intent { get; init; }
}