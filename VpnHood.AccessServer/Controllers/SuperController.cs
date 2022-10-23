using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using GrayMint.Common.AspNetCore.Auth.BotAuthentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Exceptions;
using VpnHood.AccessServer.MultiLevelAuthorization.Models;
using VpnHood.AccessServer.MultiLevelAuthorization.Repos;
using VpnHood.AccessServer.Persistence;

namespace VpnHood.AccessServer.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = BotAuthenticationDefaults.AuthenticationScheme)]

//todo
//[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + BotAuthenticationDefaults.AuthenticationScheme)]

public class SuperController<T> : ControllerBase
{
    protected readonly MultilevelAuthRepo MultilevelAuthRepo;
    protected readonly VhContext VhContext;
    protected readonly ILogger<T> Logger;

    protected SuperController(ILogger<T> logger, VhContext vhContext, MultilevelAuthRepo multilevelAuthRepo)
    {
        VhContext = vhContext;
        MultilevelAuthRepo = multilevelAuthRepo;
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
                User.Claims.FirstOrDefault(claim => claim.Type == ClaimTypes.Email)?.Value.ToLower()
                ?? throw new UnauthorizedAccessException("Could not find user's email claim!");
            return userEmail;
        }
    }

    private Guid? _userId;
    protected Guid CurrentUserId => _userId ?? throw new InvalidOperationException($"{nameof(CurrentUserId)} is not initialized yet!");

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
        await using var trans = await vhContext.WithNoLockTransaction();
        var userId = await GetCurrentUserId(vhContext);
        await MultilevelAuthRepo.SecureObject_VerifyUserPermission(secureObjectId, userId, permission);
    }
}