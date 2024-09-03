using VpnHood.AccessServer.Report.Views;

namespace VpnHood.AccessServer.Dtos.Devices;

public class DeviceData
{
    public Device Device { get; set; } = null!;
    public TrafficUsage? Usage { get; set; }
}