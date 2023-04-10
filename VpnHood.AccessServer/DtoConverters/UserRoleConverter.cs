using System;
using VpnHood.AccessServer.Dtos;

namespace VpnHood.AccessServer.DtoConverters;

public static class UserRoleConverter
{
    public static UserRole ToDto(this GrayMint.Common.AspNetCore.SimpleUserManagement.Dtos.UserRole model)
    {
        var dto = new UserRole
        {
            ResourceId = model.AppId == "*" ? Guid.Empty :Guid.Parse(model.AppId),
            Role = model.Role.ToDto(),
            User = model.User.ToDto()
        };
        return dto;
    }
}