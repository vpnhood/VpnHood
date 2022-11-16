using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.DtoConverters;

public static class AccessPointConverter
{
    public static AccessPoint ToDto(this AccessPointModel model, string? accessPointGroupName)
    {
        var accessToken = new AccessPoint
        {
            AccessPointGroupName = accessPointGroupName,
            AccessPointGroupId = model.AccessPointGroupId,
            AccessPointId = model.AccessPointId,
            AccessPointMode = model.AccessPointMode,
            IpAddress = model.IpAddress,
            IsListen = model.IsListen,
            TcpPort = model.TcpPort,
            UdpPort = model.UdpPort,
            ServerId = model.ServerId
        };
        return accessToken;
    }
}