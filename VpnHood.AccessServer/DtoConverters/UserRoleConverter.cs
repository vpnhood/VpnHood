using System;
using VpnHood.AccessServer.Dtos;

namespace VpnHood.AccessServer.DtoConverters;

public static class UserRoleConverter
{
    public static UserRole ToDto(this GrayMint.Authorization.RoleManagement.TeamControllers.Dtos.UserRole model)
    {
        var dto = new UserRole
        {
            ResourceId = model.ResourceId == "*" ? Guid.Empty : Guid.Parse(model.ResourceId),
            Role = model.Role.ToDto(),
            User = model.User?.ToDto()
        };
        return dto;
    }
}