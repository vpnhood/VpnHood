namespace VpnHood.AppLib.Contracts.Sessions;

public class AccessDevicesSummary
{
    public int DeviceCount { get; init; }
    public bool HasMoreDevices { get; init; }
    public AccessDevice[]? Devices { get; init; }
}
