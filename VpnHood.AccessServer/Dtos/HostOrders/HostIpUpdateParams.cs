using GrayMint.Common.Utils;

namespace VpnHood.AccessServer.Dtos.HostOrders;

public class HostIpUpdateParams
{
    public Patch<DateTime?>? AutoReleaseTime { get; set; }
}