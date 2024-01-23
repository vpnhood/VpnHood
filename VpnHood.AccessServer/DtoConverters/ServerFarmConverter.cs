using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.DtoConverters;

public static class ServerFarmConverter
{
    public static ServerFarm ToDto(this ServerFarmModel model, string serverProfileName)
    {
        var dto = new ServerFarm
        {
            ServerFarmName = model.ServerFarmName,
            ServerFarmId = model.ServerFarmId,
            CertificateId = model.CertificateId,
            UseHostName = model.UseHostName,
            CreatedTime = model.CreatedTime,
            ServerProfileId = model.ServerProfileId,
            ServerProfileName = serverProfileName,
            Secret = model.Secret,
            TokenUrl = model.TokenUrl != null ? new Uri(model.TokenUrl) : null,
        };

        return dto;
    }
}