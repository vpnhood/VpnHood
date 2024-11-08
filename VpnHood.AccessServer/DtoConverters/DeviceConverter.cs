using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Dtos.Devices;
using VpnHood.AccessServer.Persistence.Models;

namespace VpnHood.AccessServer.DtoConverters;

public static class DeviceConverter
{
    public static Device ToDto(this DeviceModel model)
    {
        var device = new Device {
            DeviceId = model.DeviceId,
            ClientId = model.ClientId.ToString(),
            ClientVersion = model.ClientVersion,
            CreatedTime = model.CreatedTime,
            IpAddress = model.IpAddress,
            LockedTime = model.LockedTime,
            Location = model.Country !=null ? new Location {
                CountryCode = model.Country,
                RegionName = null,
                CityName = null
            } : null,
            ModifiedTime = model.LastUsedTime,
            UserAgent = model.UserAgent
        };
        return device;
    }
}