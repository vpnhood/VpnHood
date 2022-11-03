namespace VpnHood.AccessServer.Dtos;

public class DeviceData
{
    public Device Device { get; set; } = null!;
    public TrafficUsage? Usage { get; set; }
}