using VpnHood.AccessServer.Dtos;

namespace VpnHood.AccessServer.DtoConverters;

public static class UserRoleConverter
{
    public static UserRole ToDto(this GrayMint.Common.AspNetCore.SimpleUserManagement.Dtos.UserRole<UserExData> model)
    {
        var dto = new UserRole
        {
            AppId = model.AppId,
            Role = model.Role.ToDto(),
            User = model.User.ToDto()
        };
        return dto;
    }
}