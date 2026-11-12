namespace VpnHood.Core.Client.Devices.Abstractions;

/// <summary>
/// Represents the device's memory.
/// </summary>
public class DeviceMemInfo
{
    /// <summary>Gets the device's memory, in bytes, as the platform reported it.</summary>
    public required long TotalMemory { get; init; }

    /// <summary>Gets the memory available, in bytes, as the platform reported it.</summary>
    public required long AvailableMemory { get; init; }
}