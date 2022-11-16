using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.DtoConverters;

public static class DeviceConverter
{
    public static Device ToDto(this DeviceModel model)
    {
        var device = new Device
        {
            DeviceId = model.DeviceId,
            ClientId = model.ClientId,
            ClientVersion = model.ClientVersion,
            CreatedTime = model.CreatedTime,
            IpAddress = model.IpAddress,
            LockedTime = model.LockedTime,
            ModifiedTime = model.ModifiedTime,
            UserAgent = model.UserAgent,
        };
        return device;
    }
}

