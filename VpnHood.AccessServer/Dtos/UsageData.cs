using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Dtos;

public class UsageData
{
    public Usage? Usage { get; set; } = default!;
    public AccessUsageModel LastAccessUsage { get; set; } = default!;
}