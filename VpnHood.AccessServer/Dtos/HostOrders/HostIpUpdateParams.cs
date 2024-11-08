using GrayMint.Common.Utils;

namespace VpnHood.AccessServer.Dtos.HostOrders;

public class HostIpUpdateParams
{
    public Patch<DateTime?>? AutoReleaseTime { get; set; }
    public Patch<bool>? IsHidden { get; set; }
    public Patch<string?>? ProviderDescription { get; set; }
    public Patch<string?>? Description { get; set; }
}