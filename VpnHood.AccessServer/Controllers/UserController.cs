using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Models;

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
            await using VhContext vhContext = new();
            return await vhContext.Users.SingleAsync(x => x.Email == AuthUserEmail);

        }

        [HttpPost("current/register")]
        public async Task<User> RegisterCurrentUser()
        {
            await using VhContext vhContext = new();

            var userEmail =
                User.Claims.FirstOrDefault(claim => claim.Type == "emails")?.Value.ToLower()
                ?? throw new UnauthorizedAccessException("Could not find user's email claim!");

            var user = new User
            {
                UserId = Guid.NewGuid(),
                AuthUserId = AuthUserId,
                Email = userEmail,
                CreatedTime = DateTime.UtcNow
            };

            await vhContext.Users.AddAsync(user);
            await vhContext.SaveChangesAsync();
            return user;
        }

    }
}