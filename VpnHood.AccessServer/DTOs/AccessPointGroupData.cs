using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.DTOs
{
    public class AccessPointGroupData
    {
        public AccessPointGroup AccessPointGroup { get; set; } = null!;
        public AccessPoint? DefaultAccessPoint { get; set; }
    }
}