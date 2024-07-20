using VpnHood.AccessServer.Dtos.ServerFarms;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.AccessServer.Persistence.Utils;

namespace VpnHood.AccessServer.DtoConverters;

public static class ServerFarmConverter
{
    public static ServerFarm ToDto(this ServerFarmModel model, string serverProfileName)
    {
        var dto = new ServerFarm
        {
            ServerFarmName = model.ServerFarmName,
            ServerFarmId = model.ServerFarmId,
            UseHostName = model.UseHostName,
            CreatedTime = model.CreatedTime,
            ServerProfileId = model.ServerProfileId,
            ServerProfileName = serverProfileName,
            Secret = model.Secret,
            TokenUrl = string.IsNullOrEmpty(model.TokenUrl) ? null : new Uri(model.TokenUrl),
            TokenError = model.TokenError,
            PushTokenToClient = model.PushTokenToClient,
            MaxCertificateCount = model.MaxCertificateCount,
            Certificate = model.Certificates!=null ? model.GetCertificateInToken().ToDto() : null
        };

        return dto;
    }
}