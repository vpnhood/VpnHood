using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.DtoConverters;

public static class AccessTokenConverter
{
    public static AccessToken ToDto(this AccessTokenModel model, string? serverFarmName)
    {
        var accessToken = new AccessToken
        {
            ServerFarmId = model.ServerFarmId,
            AccessTokenId = model.AccessTokenId,
            ServerFarmName = serverFarmName,
            AccessTokenName = model.AccessTokenName,
            CreatedTime = model.CreatedTime,
            ModifiedTime = model.ModifiedTime,
            FirstUsedTime = model.FirstUsedTime,
            LastUsedTime = model.LastUsedTime,
            ExpirationTime = model.ExpirationTime,
            IsPublic = model.IsPublic,
            IsEnabled = model.IsEnabled,
            Lifetime = model.Lifetime,
            MaxDevice = model.MaxDevice,
            MaxTraffic = model.MaxTraffic,
            ProjectId = model.ProjectId,
            SupportCode = model.SupportCode,
            Url = model.Url
        };
        return accessToken;
    }
}