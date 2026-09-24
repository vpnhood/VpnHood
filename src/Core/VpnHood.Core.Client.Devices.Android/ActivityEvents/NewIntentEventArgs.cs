using Android.Content;

namespace VpnHood.Core.Client.Devices.Android.ActivityEvents;

public class NewIntentEventArgs
{
    public required Intent? Intent { get; init; }
}