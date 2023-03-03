using VpnHood.AccessServer.Dtos.ServerFarmDtos;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.DtoConverters;

public static class ServerFarmConverter
{
    public static ServerFarm ToDto(this ServerFarmModel model)
    {
        var dto = new ServerFarm
        {
            ServerFarmName = model.ServerFarmName,
            ServerFarmId = model.ServerFarmId,
            CertificateId = model.CertificateId,
            CreatedTime = model.CreatedTime
        };

        return dto;
    }
}