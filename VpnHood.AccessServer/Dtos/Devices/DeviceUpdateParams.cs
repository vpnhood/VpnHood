using GrayMint.Common.Utils;

namespace VpnHood.AccessServer.Dtos.Devices;

public class DeviceUpdateParams
{
    public Patch<bool>? IsLocked { get; set; }
}