using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.DTOs
{
    public class ProjectClientData
    {
        public Usage Usage { get; set; } = new Usage();
        public AccessUsageEx LastAccessUsage { get; set; } = default!;
    }
}