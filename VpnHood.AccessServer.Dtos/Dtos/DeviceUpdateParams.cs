using VpnHood.Common.Utils;

namespace VpnHood.AccessServer.Dtos;

public class DeviceUpdateParams
{
    public Patch<bool>? IsLocked { get; set; }
}