using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.DTOs;
using VpnHood.AccessServer.Exceptions;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Security;
using VpnHood.Common;

namespace VpnHood.AccessServer.Controllers;

[Route("/api/projects")]
public class ProjectController : SuperController<ProjectController>
{
    private readonly IMemoryCache _memoryCache;
    public ProjectController(ILogger<ProjectController> logger, IMemoryCache memoryCache) : base(logger)
    {
        _memoryCache = memoryCache;
    }

    [HttpGet("{projectId:guid}")]
    public async Task<Project> Get(Guid projectId)
    {
        await using var vhContext = new VhContext();
        await VerifyUserPermission(vhContext, projectId, Permissions.ProjectRead);

        var query = vhContext.Projects
            .Include(x => x.ProjectRoles);

        var res = await query
            .SingleAsync(e => e.ProjectId == projectId);
        return res;
    }

    [HttpPost]
    public async Task<Project> Create(Guid? projectId = null)
    {
        projectId ??= Guid.NewGuid();
        await using var vhContext = new VhContext();
        var curUserId = await GetCurrentUserId(vhContext);
        await VerifyUserPermission(vhContext, curUserId, Permissions.ProjectCreate);

        // Check user quota
        using var singleRequest = SingleRequest.Start($"CreateProject_{curUserId}");

        // get user's maxProjects quota
        var user = await vhContext.Users.SingleAsync(x => x.UserId == curUserId);

        // find all user's project with owner role
        var query =
            from projectRole in vhContext.ProjectRoles
            join secureObjectRolePermission in vhContext.SecureObjectRolePermissions on projectRole.RoleId equals secureObjectRolePermission.RoleId
            join userRole in vhContext.RoleUsers on projectRole.RoleId equals userRole.RoleId
            where
                secureObjectRolePermission.PermissionGroupId == PermissionGroups.ProjectOwner.PermissionGroupId &&
                userRole.UserId == user.UserId
            select projectRole.ProjectId;

        var userProjectOwnerCount = await query.Distinct().CountAsync();
        if (userProjectOwnerCount >= user.MaxProjectCount)
        {
            throw new QuotaException(nameof(VhContext.Projects), user.MaxProjectCount);
        }

        // Roles
        var ownersRole = await vhContext.AuthManager.Role_Create(Resource.ProjectOwners, curUserId);
        var viewersRole = await vhContext.AuthManager.Role_Create(Resource.ProjectViewers, curUserId);

        // Groups
        AccessPointGroup accessPointGroup = new()
        {
            AccessPointGroupId = Guid.NewGuid(),
            AccessPointGroupName = "Group1",
            Certificate = CertificateController.CreateInternal(projectId.Value, null)
        };

        // create project
        Project project = new()
        {
            ProjectId = projectId.Value,
            AccessPointGroups = new HashSet<AccessPointGroup>
            {
                accessPointGroup,
            },
            AccessTokens = new HashSet<AccessToken>
            {
                new()
                {
                    AccessTokenId = Guid.NewGuid(),
                    AccessPointGroup = accessPointGroup,
                    AccessTokenName = "Public",
                    SupportCode = 1000,
                    Secret = Util.GenerateSessionKey(),
                    IsPublic = true
                },

                new()
                {
                    AccessTokenId = Guid.NewGuid(),
                    AccessPointGroup = accessPointGroup,
                    AccessTokenName = "Private 1",
                    IsPublic = false,
                    SupportCode = 1001,
                    MaxDevice = 5,
                    Secret = Util.GenerateSessionKey()
                }
            },
            ProjectRoles = new HashSet<ProjectRole>
            {
                new()
                {
                    RoleId = ownersRole.RoleId
                },
                new()
                {
                    RoleId = viewersRole.RoleId
                }
            }
        };

        await vhContext.Projects.AddAsync(project);

        // Grant permissions
        var secureObject = await vhContext.AuthManager.CreateSecureObject(projectId.Value, SecureObjectTypes.Project);
        await vhContext.AuthManager.SecureObject_AddRolePermission(secureObject, ownersRole, PermissionGroups.ProjectOwner, curUserId);
        await vhContext.AuthManager.SecureObject_AddRolePermission(secureObject, viewersRole, PermissionGroups.ProjectViewer, curUserId);

        // add current user as the admin
        await vhContext.AuthManager.Role_AddUser(ownersRole.RoleId, curUserId, curUserId);

        await vhContext.SaveChangesAsync();
        return project;
    }

    [HttpGet]
    public async Task<Project[]> List()
    {
        await using var vhContext = new VhContext();
        var curUserId = await GetCurrentUserId(vhContext);
        await VerifyUserPermission(vhContext, curUserId, Permissions.ProjectList);

        var query =
            from project in vhContext.Projects
            join projectRole in vhContext.ProjectRoles on project.ProjectId equals projectRole.ProjectId
            join roleUser in vhContext.RoleUsers on projectRole.RoleId equals roleUser.RoleId
            where roleUser.UserId == curUserId
            select project;

        var ret = await query
            .Distinct()
            .ToArrayAsync();

        return ret;
    }

    [HttpGet("usage-summary")]
    public async Task<Usage> GetUsageSummary(Guid projectId, DateTime? startTime = null, DateTime? endTime = null)
    {
        await using var vhContext = new VhContext();
        await VerifyUserPermission(vhContext, projectId, Permissions.ProjectRead);

        // check cache if it ends till now and duration is more than 3 hours
        string? cacheKey = null;
        TimeSpan? cacheExpiration = null;
        if (endTime == null || DateTime.UtcNow - endTime < TimeSpan.FromMinutes(2))
        {
            var duration = (endTime ?? DateTime.UtcNow) - (startTime ?? DateTime.UtcNow.AddYears(-2));
            cacheKey = $"project_usage_{projectId}_{(long)(duration.TotalMinutes / 30)}";
            cacheExpiration = TimeSpan.FromMinutes(Math.Min(60*24, duration.TotalMinutes / 30));
        }
        
        if (cacheKey != null && _memoryCache.TryGetValue(cacheKey, out Usage cacheRes))
             return cacheRes;
        

        // select and order
        var query =
            from accessUsage in vhContext.AccessUsages
            where
                (accessUsage.ProjectId == projectId) &&
                (startTime == null || accessUsage.CreatedTime >= startTime) &&
                (endTime == null || accessUsage.CreatedTime <= endTime)
            group new { accessUsage } by true into g
            select new Usage
            {
                DeviceCount = g.Select(y => y.accessUsage.DeviceId).Distinct().Count(),
                SentTraffic = g.Sum(y => y.accessUsage.SentTraffic),
                ReceivedTraffic = g.Sum(y => y.accessUsage.ReceivedTraffic),
            };

        var res = await query.SingleOrDefaultAsync() ?? new Usage { ServerCount = 0, DeviceCount = 0 };

        // update cache
        if (cacheExpiration != null)
            _memoryCache.Set(cacheKey, res, cacheExpiration.Value);

        return res;
    }

    [HttpGet("usage/live-usage-summary")]
    public async Task<LiveUsageSummary> GeLiveUsageSummary(Guid projectId)
    {
        await using var vhContext = new VhContext();
        await VerifyUserPermission(vhContext, projectId, Permissions.ProjectRead);

        var lostThresholdTime = DateTime.UtcNow.Subtract(AccessServerApp.Instance.LostServerThreshold);
        var query =
            from server in vhContext.Servers
            join serverStatus in vhContext.ServerStatuses on
                new { key1 = server.ServerId, key2 = true } equals new { key1 = serverStatus.ServerId, key2 = serverStatus.IsLast } into g0
            from serverStatus in g0.DefaultIfEmpty()
            where server.ProjectId == projectId
            group new { server, serverStatus } by true into g
            select new LiveUsageSummary
            {
                TotalServerCount = g.Count(),
                NotInstalledServerCount = g.Count(x => x.serverStatus == null),
                ActiveServerCount = g.Count(x => x.serverStatus.CreatedTime > lostThresholdTime && x.serverStatus.SessionCount > 0),
                IdleServerCount = g.Count(x => x.serverStatus.CreatedTime > lostThresholdTime && x.serverStatus.SessionCount == 0),
                LostServerCount = g.Count(x => x.serverStatus.CreatedTime < lostThresholdTime),
                SessionCount = g.Where(x => x.serverStatus.CreatedTime > lostThresholdTime).Sum(x => x.serverStatus.SessionCount),
                TunnelSendSpeed = g.Where(x => x.serverStatus.CreatedTime > lostThresholdTime).Sum(x => x.serverStatus.TunnelSendSpeed),
                TunnelReceiveSpeed = g.Where(x => x.serverStatus.CreatedTime > lostThresholdTime).Sum(x => x.serverStatus.TunnelReceiveSpeed)
            };

        var res = await query.SingleOrDefaultAsync() ?? new LiveUsageSummary();
        return res;
    }
}