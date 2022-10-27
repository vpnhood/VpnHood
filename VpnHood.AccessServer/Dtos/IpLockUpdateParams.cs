using GrayMint.Common.Client;

namespace VpnHood.AccessServer.Dtos;

public class IpLockUpdateParams
{
    public Patch<bool>? IsLocked { get; set; }
    public Patch<string?>? Description { get; set; }
}