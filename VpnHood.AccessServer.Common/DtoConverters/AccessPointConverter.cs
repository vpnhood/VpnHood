using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.DtoConverters;

public static class AccessPointConverter
{
    public static AccessPoint ToDto(this AccessPointModel model)
    {
        var dto = new AccessPoint
        {
            UdpPort = model.UdpPort,
            AccessPointMode = model.AccessPointMode,
            TcpPort = model.TcpPort,
            IpAddress = model.IpAddress,
            IsListen = model.IsListen
        };
        return dto;
    }

    public static AccessPointModel ToModel(this AccessPoint dto)
    {
        var model = new AccessPointModel
        {
            UdpPort = dto.UdpPort,
            AccessPointMode = dto.AccessPointMode,
            TcpPort = dto.TcpPort,
            IpAddress = dto.IpAddress,
            IsListen = dto.IsListen
        };
        return model;
    }
}