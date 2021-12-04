using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.DTOs
{
    public class AccessData
    {
        public Access Access { get; set; } = default!;
        public Usage? Usage { get; set; } = new Usage();
        public AccessUsageEx? LastAccessUsage { get; set; } = default!;
    }
}