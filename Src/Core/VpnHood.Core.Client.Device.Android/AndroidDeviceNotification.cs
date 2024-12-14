namespace VpnHood.Core.Client.Device.Droid;

public class AndroidDeviceNotification
{
    public required int NotificationId { get; init; }
    public required Notification Notification { get; init; }
}