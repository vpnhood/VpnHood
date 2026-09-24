namespace VpnHood.Core.Client.Devices.Abstractions;

public class DeviceMemInfo
{
    public required long TotalMemory { get; init; }
    public required long AvailableMemory { get; init; }
}