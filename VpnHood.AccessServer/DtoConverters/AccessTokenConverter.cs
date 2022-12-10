using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.DtoConverters;

public static class AccessTokenConverter
{
    public static AccessToken ToDto(this AccessTokenModel model, string? accessPointGroupName)
    {
        var accessToken = new AccessToken
        {
            AccessPointGroupId = model.AccessPointGroupId,
            AccessTokenId = model.AccessTokenId,
            AccessPointGroupName = accessPointGroupName,
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