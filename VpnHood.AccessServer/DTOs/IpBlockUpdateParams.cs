namespace VpnHood.AccessServer.DTOs
{
    public class IpBlockUpdateParams
    {
        public Patch<bool>? IsLocked { get; set; }
        public Patch<string?>? Description { get; set; }
    }
}