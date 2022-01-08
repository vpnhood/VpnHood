using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.DTOs;

public class AccessData
{
    public Access Access { get; set; } = default!;
    public AccessStatus AccessStatus { get; set; }

    public Usage? Usage { get; set; } = new ();
    public AccessUsageEx? LastAccessUsage { get; set; } = default!;
}