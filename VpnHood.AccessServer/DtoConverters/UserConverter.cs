using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.DtoConverters;

public static class UserConverter
{
    public static User ToDto(this UserModel model)
    {
        var user = new User
        {
            CreatedTime = model.CreatedTime,
            Email= model.Email,
            MaxProjectCount= model.MaxProjectCount,
            UserId=model.UserId,
            UserName = model.UserName
        };
        return user;
    }
}

public static class ServerProfileConverter
{
    public static ServerProfile ToDto(this ServerProfileModel model)
    {
        return new ServerProfile
        {
            ServerProfileId = model.ServerProfileId,
            ServerConfig = model.ServerConfig
        };
    }
}