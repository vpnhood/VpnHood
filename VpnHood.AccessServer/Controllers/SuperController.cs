using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Exceptions;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.MultiLevelAuthorization.Models;
using VpnHood.AccessServer.MultiLevelAuthorization.Services;
using VpnHood.AccessServer.Persistence;

namespace VpnHood.AccessServer.Controllers;

[ApiController]
[Authorize]
public class SuperController<T> : ControllerBase
{
    protected readonly MultilevelAuthService MultilevelAuthService;
    protected readonly VhContext VhContext;
    protected readonly ILogger<T> Logger;

    protected SuperController(ILogger<T> logger, VhContext vhContext, MultilevelAuthService multilevelAuthService)
    {
        VhContext = vhContext;
        MultilevelAuthService = multilevelAuthService;
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

    protected async Task<ProjectModel> GetProject(Guid projectId)
    {
        var project = await VhContext.Projects.FindAsync(projectId);
        return project ?? throw new KeyNotFoundException($"Could not find project. ProjectId: {projectId}");
    }
    
    protected async Task VerifyUserPermission(Guid secureObjectId, Permission permission)
    {
        await using var trans = await VhContext.WithNoLockTransaction();
        var userId = await GetCurrentUserId(VhContext);
        await MultilevelAuthService.SecureObject_VerifyUserPermission(secureObjectId, userId, permission);
    }

    protected async Task VerifyUsageQueryPermission(Guid projectId, DateTime? usageBeginTime, DateTime? usageEndTime)
    {
        var projectModel = await GetProject(projectId);
        usageBeginTime ??= DateTime.UtcNow;
        usageEndTime ??= DateTime.UtcNow;
        var requestTimeSpan = usageEndTime - usageBeginTime;
        var maxTimeSpan = projectModel.SubscriptionType == SubscriptionType.Free
            ? QuotaConstants.UsageQueryTimeSpanFree
            : QuotaConstants.UsageQueryTimeSpanPremium;

        if (requestTimeSpan > maxTimeSpan)
            throw new QuotaException("UsageQuery", (long)maxTimeSpan.TotalHours, "The usage query period is not supported by your plan.");
    }
}