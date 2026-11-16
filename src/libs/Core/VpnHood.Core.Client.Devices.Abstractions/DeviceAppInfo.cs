namespace VpnHood.Core.Client.Devices.Abstractions;

/// <summary>
/// Represents an app installed on the device, which a split by app can pick.
/// </summary>
public class DeviceAppInfo
{
    /// <summary>Gets the app's ID, as its package name on Android.</summary>
    public required string AppId { get; init; }

    /// <summary>Gets the app's name.</summary>
    public required string AppName { get; init; }

    /// <summary>Gets the app's icon, a PNG in Base64.</summary>
    public required string IconPng { get; init; }
}