using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Dtos;

public class UsageData
{
    public Usage? Usage { get; set; } = default!;
    public AccessUsageEx LastAccessUsage { get; set; } = default!;
}