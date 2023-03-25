using VpnHood.AccessServer.Dtos.UserDtos;

namespace VpnHood.AccessServer.DtoConverters;

public static class RoleConverter
{
    public static Role ToDto(this GrayMint.Common.AspNetCore.SimpleUserManagement.Dtos.Role model)
    {
        var dto = new Role
        {
            RoleId = model.RoleId,
            RoleName = model.RoleName,
            Description = model.Description,
        };
        return dto;
    }
}