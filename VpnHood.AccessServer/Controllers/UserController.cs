using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.MultiLevelAuthorization.Services;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Security;

namespace VpnHood.AccessServer.Controllers;

[Route("/api/v{version:apiVersion}/users")]
public class UserController : SuperController<UserController>
{
    public UserController(ILogger<UserController> logger, VhContext vhContext, MultilevelAuthService multilevelAuthService)
        : base(logger, vhContext, multilevelAuthService)
    {
    }

    [HttpPost("current")]
    public async Task<User> GetCurrentUser()
    {
        var userModel = await VhContext.Users.SingleAsync(x => x.Email == AuthUserEmail);
        return userModel.ToDto();
    }

    [HttpPost("current/register")]
    public async Task<User> RegisterCurrentUser()
    {
        var userEmail =
            User.Claims.FirstOrDefault(claim => claim.Type == ClaimTypes.Email)?.Value.ToLower()
            ?? throw new UnauthorizedAccessException("Could not find user's email claim!");

        var user = new UserModel
        {
            UserId = Guid.NewGuid(),
            AuthUserId = AuthUserId,
            Email = userEmail,
            CreatedTime = DateTime.UtcNow,
            MaxProjectCount = QuotaConstants.ProjectCount
        };

        await VhContext.Users.AddAsync(user);
        var secureObject = await MultilevelAuthService.CreateSecureObject(user.UserId, SecureObjectTypes.User);
        await MultilevelAuthService.SecureObject_AddUserPermission(secureObject, user.UserId, PermissionGroups.UserBasic, user.UserId);
        
        await VhContext.SaveChangesAsync();
        
        return user.ToDto();
    }

    [HttpGet("{userId:guid}")]
    public async Task<User> Get(Guid userId)
    {
        await VerifyUserPermission(userId, Permissions.UserRead);

        var userModel = await VhContext.Users.SingleAsync(x=>x.UserId == userId);
        return userModel.ToDto();
    }

    [HttpPatch("{userId:guid}")]
    public async Task<User> Update(Guid userId, UserUpdateParams updateParams)
    {
        await VerifyUserPermission(userId, Permissions.UserWrite);
        var user = await VhContext.Users.SingleAsync(x => x.UserId == userId);

        if (updateParams.MaxProjects != null) user.MaxProjectCount = updateParams.MaxProjects;
        await VhContext.SaveChangesAsync();

        return user.ToDto();
    }
}