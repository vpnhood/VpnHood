using VpnHood.AccessServer.Dtos;

namespace VpnHood.AccessServer.DtoConverters;

public static class UserConverter
{
    public static User ToDto(this GrayMint.Common.AspNetCore.SimpleUserManagement.Dtos.User model)
    {
        var dto = new User
        {
            UserId  = model.UserId,
            AccessedTime = model.AccessedTime,
            Email = model.Email,
            FirstName   = model.FirstName,
            LastName = model.LastName,
            IsBot = model.IsBot,
            CreatedTime = model.CreatedTime,
            Description = model.Description
        };
        return dto;
    }
}