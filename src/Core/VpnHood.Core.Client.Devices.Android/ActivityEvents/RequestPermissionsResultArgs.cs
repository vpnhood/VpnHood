using Android.Content.PM;

namespace VpnHood.Core.Client.Devices.Android.ActivityEvents;

public class RequestPermissionsResultArgs
{
    public required int RequestCode { get; init; }
    public required string[] Permissions { get; init; }
    public required Permission[] GrantResults { get; init; }
}