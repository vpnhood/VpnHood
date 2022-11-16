using System;
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
            EndTime = model.EndTime,
            IsPublic = model.IsPublic,
            Lifetime = model.Lifetime,
            MaxDevice = model.MaxDevice,
            MaxTraffic = model.MaxTraffic,
            ProjectId = model.ProjectId,
            StartTime = model.StartTime,
            SupportCode = model.SupportCode,
            Url = model.Url
        };
        return accessToken;
    }
}