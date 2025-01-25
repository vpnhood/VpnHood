namespace VpnHood.Core.Common.Messaging;

public class AccessDevicesSummary
{
    public int DeviceCount { get; init; } // The count of devices
    public bool HasMoreDevices { get; init; } // If true, there are more devices than the count
    public AccessDevice[]? Devices { get; init; }
}