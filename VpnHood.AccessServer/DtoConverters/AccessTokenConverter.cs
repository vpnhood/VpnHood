using VpnHood.AccessServer.Dtos.AccessTokens;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.Manager.Common.Utils;

namespace VpnHood.AccessServer.DtoConverters;

public static class AccessTokenConverter
{
    public static AccessToken ToDto(this AccessTokenModel model, string? serverFarmName)
    {
        var accessToken = new AccessToken {
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
            ClientPolicies = model.ClientPoliciesGet().ToDtos(),
            AccessCode = model.AccessCode,
            ManagerCode = model.ManagerCode,
            IsEnabled = model.IsEnabled,
            Lifetime = model.Lifetime,
            MaxDevice = model.MaxDevice,
            MaxTraffic = model.MaxTraffic,
            Tags = TagUtils.TagsFromString(model.Tags),
            ProjectId = model.ProjectId,
            SupportCode = model.SupportCode,
            AdRequirement = model.AdRequirement,
            Description = model.Description
        };
        return accessToken;
    }
}

