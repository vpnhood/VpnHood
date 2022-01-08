namespace VpnHood.AccessServer.DTOs;

public class IpLockUpdateParams
{
    public Patch<bool>? IsLocked { get; set; }
    public Patch<string?>? Description { get; set; }
}