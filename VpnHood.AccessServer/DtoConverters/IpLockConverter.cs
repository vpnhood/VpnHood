using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.DtoConverters;

public static class IpLockConverter
{
    public static IpLock ToDto(this IpLockModel model)
    {
        var ipLock = new IpLock
        {
            Description = model.Description,
            IpAddress = model.IpAddress,
            LockedTime = model.LockedTime
        };
        return ipLock;
    }
}