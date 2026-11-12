namespace VpnHood.Core.Common.Messaging;

/// <summary>
/// Represents the devices that used an access.
/// </summary>
public class AccessDevicesSummary
{
    /// <summary>Gets the number of devices.</summary>
    public int DeviceCount { get; init; }

    /// <summary>Gets a value indicating whether there are more devices than <see cref="DeviceCount"/>.</summary>
    public bool HasMoreDevices { get; init; }

    /// <summary>Gets the devices, or <see langword="null"/>.</summary>
    public AccessDevice[]? Devices { get; init; }
}