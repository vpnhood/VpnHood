namespace VpnHood.AppLib.Api.Sessions;

public class AccessDevicesSummary
{
    public int DeviceCount { get; init; }
    public bool HasMoreDevices { get; init; }
    public IReadOnlyList<AccessDevice>? Devices { get; init; }
}
