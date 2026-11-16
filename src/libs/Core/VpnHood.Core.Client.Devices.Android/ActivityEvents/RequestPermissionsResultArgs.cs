using Android.Content.PM;

namespace VpnHood.Core.Client.Devices.Android.ActivityEvents;

/// <summary>
/// Provides the data of <see cref="IActivityEvent.RequestPermissionsResultEvent"/>.
/// </summary>
public class RequestPermissionsResultArgs
{
    /// <summary>Gets the code the request was made with.</summary>
    public required int RequestCode { get; init; }

    /// <summary>Gets the permissions asked for.</summary>
    public required string[] Permissions { get; init; }

    /// <summary>Gets whether each permission was granted, in the order of <see cref="Permissions"/>.</summary>
    public required Permission[] GrantResults { get; init; }
}