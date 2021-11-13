using System;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.DTOs
{
    public class DeviceData
    {
        public Guid KeyId { get; set; }
        public Usage Usage { get; set; } = new Usage();
        public AccessUsageEx LastAccessUsage { get; set; } = default!;
    }
}