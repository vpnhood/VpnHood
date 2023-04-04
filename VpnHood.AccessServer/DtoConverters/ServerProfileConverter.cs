using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.DtoConverters;

public static class ServerProfileConverter
{
    public static ServerProfile ToDto(this ServerProfileModel model)
    {
        return new ServerProfile
        {
            ServerProfileId = model.ServerProfileId,
            ServerProfileName = model.ServerProfileName,
            IsDefault = model.IsDefault,
            ServerConfig = model.ServerConfig,
            CreatedTime = model.CreatedTime
        };
    }
}