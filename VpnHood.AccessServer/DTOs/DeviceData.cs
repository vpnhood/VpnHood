using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.DTOs
{
    public class DeviceData
    {
        public Device Device { get; set; } = null!;
        public TrafficUsage? Usage { get; set; }
        public string OsName => UserAgentParser.GetOperatingSystem(Device.UserAgent);
    }
}