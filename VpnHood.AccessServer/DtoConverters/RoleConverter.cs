using VpnHood.AccessServer.Dtos;

namespace VpnHood.AccessServer.DtoConverters;

public static class RoleConverter
{
    public static Role ToDto(this GrayMint.Authorization.RoleManagement.TeamControllers.Dtos.Role model)
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