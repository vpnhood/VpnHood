using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Authorization;
using VpnHood.AccessServer.Authorization.Models;
using VpnHood.AccessServer.DTOs;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Security;

namespace VpnHood.AccessServer.Controllers
{
    [Route("/api/users")]
    public class UserController : SuperController<UserController>
    {
        public UserController(ILogger<UserController> logger) : base(logger)
        {
        }

        [HttpPost("current")]
        public async Task<User> GetCurrentUser()
        {
            await using var vhContext = new VhContext();
            return await vhContext.Users.SingleAsync(x => x.Email == AuthUserEmail);
        }

        [HttpPost("current/register")]
        public async Task<User> RegisterCurrentUser()
        {
            await using var vhContext = new VhContext();

            var userEmail =
                User.Claims.FirstOrDefault(claim => claim.Type == "emails")?.Value.ToLower()
                ?? throw new UnauthorizedAccessException("Could not find user's email claim!");

            var user = new User
            {
                UserId = Guid.NewGuid(),
                AuthUserId = AuthUserId,
                Email = userEmail,
                CreatedTime = DateTime.UtcNow,
                MaxProjectCount = AccessServerApp.Instance.UserMaxProjectCount
            };

            await vhContext.Users.AddAsync(user);
            var secureObject = await vhContext.AuthManager.CreateSecureObject(user.UserId, SecureObjectTypes.User);
            await vhContext.AuthManager.SecureObject_AddUserPermission(secureObject, user.UserId, PermissionGroups.ProjectViewer, user.UserId);
            await vhContext.SaveChangesAsync();
            return user;
        }

        [HttpGet("{userId:guid}")]
        public async Task<User> Get(Guid userId)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, userId, Permissions.UserRead);
            var user = await vhContext.Users.SingleAsync(x=>x.UserId == userId);
            return user;
        }

        [HttpPatch("{userId:guid}")]
        public async Task<User> Update(Guid userId, UserUpdateParams updateParams)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, userId, Permissions.UserWrite);
            var user = await vhContext.Users.SingleAsync(x => x.UserId == userId);

            if (updateParams.MaxProjects != null) user.MaxProjectCount = updateParams.MaxProjects;
            await vhContext.SaveChangesAsync();
            return user;
        }
    }
}