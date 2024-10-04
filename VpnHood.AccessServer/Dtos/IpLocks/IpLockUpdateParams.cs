using GrayMint.Common.Utils;

namespace VpnHood.AccessServer.Dtos.IpLocks;

public class IpLockUpdateParams
{
    public Patch<bool>? IsLocked { get; set; }
    public Patch<string?>? Description { get; set; }
}