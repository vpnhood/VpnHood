using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Authorization.Models;
using VpnHood.AccessServer.Exceptions;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Controllers
{
    [ApiController]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + ",Robot")]
    public class SuperController<T> : ControllerBase
    {
        protected readonly ILogger<T> Logger;

        protected SuperController(ILogger<T> logger)
        {
            Logger = logger;
        }

        protected string AuthUserId
        {
            get
            {
                var issuer = User.Claims.FirstOrDefault(claim => claim.Type == "iss")?.Value ?? throw new UnauthorizedAccessException();
                var sub = User.Claims.FirstOrDefault(claim => claim.Type == ClaimTypes.NameIdentifier)?.Value ?? throw new UnauthorizedAccessException();
                return issuer + ":" + sub;
            }
        }

        protected string AuthUserEmail
        {
            get
            {
                var userEmail =
                    User.Claims.FirstOrDefault(claim => claim.Type == "emails")?.Value.ToLower()
                    ?? throw new UnauthorizedAccessException("Could not find user's email claim!");
                return userEmail;
            }
        }

        private Guid? _userId;
        protected async Task<Guid> GetCurrentUserId(VhContext vhContext)
        {
            // use cache
            if (_userId != null)
                return _userId.Value;

            // find user by email
            var userEmail = AuthUserEmail;
            var ret = 
                await vhContext.Users.SingleOrDefaultAsync(x => x.Email == userEmail)
                ?? throw new UnregisteredUser($"Could not find any user with given email. email: {userEmail}!");

            _userId = ret.UserId;
            return ret.UserId;
        }

        protected async Task VerifyUserPermission(VhContext vhContext, Guid secureObjectId, Permission permission)
        {
            var userId = await GetCurrentUserId(vhContext);
            await vhContext.AuthManager.SecureObject_VerifyUserPermission(secureObjectId, userId, permission);
        }
    }
}