using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.MultiLevelAuthorization.Repos;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Security;

namespace VpnHood.AccessServer.Controllers;

[Route("/api/users")]
public class UserController : SuperController<UserController>
{
    public UserController(ILogger<UserController> logger, VhContext vhContext, MultilevelAuthRepo multilevelAuthRepo)
        : base(logger, vhContext, multilevelAuthRepo)
    {
    }

    [HttpPost("current")]
    public async Task<User> GetCurrentUser()
    {
        return await VhContext.Users.SingleAsync(x => x.Email == AuthUserEmail);
    }

    [HttpPost("current/register")]
    public async Task<User> RegisterCurrentUser()
    {
        var userEmail =
            User.Claims.FirstOrDefault(claim => claim.Type == ClaimTypes.Email)?.Value.ToLower()
            ?? throw new UnauthorizedAccessException("Could not find user's email claim!");

        var user = new User
        {
            UserId = Guid.NewGuid(),
            AuthUserId = AuthUserId,
            Email = userEmail,
            CreatedTime = DateTime.UtcNow,
            MaxProjectCount = QuotaConstants.ProjectCount
        };

        await VhContext.Users.AddAsync(user);
        var secureObject = await MultilevelAuthRepo.CreateSecureObject(user.UserId, SecureObjectTypes.User);
        await MultilevelAuthRepo.SecureObject_AddUserPermission(secureObject, user.UserId, PermissionGroups.UserBasic, user.UserId);
        
        await VhContext.SaveChangesAsync();
        return user;
    }

    [HttpGet("{userId:guid}")]
    public async Task<User> Get(Guid userId)
    {
        await VerifyUserPermission(VhContext, userId, Permissions.UserRead);

        var user = await VhContext.Users.SingleAsync(x=>x.UserId == userId);
        return user;
    }

    [HttpPatch("{userId:guid}")]
    public async Task<User> Update(Guid userId, UserUpdateParams updateParams)
    {
        await VerifyUserPermission(VhContext, userId, Permissions.UserWrite);
        var user = await VhContext.Users.SingleAsync(x => x.UserId == userId);

        if (updateParams.MaxProjects != null) user.MaxProjectCount = updateParams.MaxProjects;
        await VhContext.SaveChangesAsync();
        return user;
    }
}