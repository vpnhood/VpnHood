﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    private readonly IOptions<AppOptions> _appOptions;
    private readonly VhReportContext _vhReportContext;

    public ProjectController(ILogger<ProjectController> logger, 
        VhContext vhContext, 
        VhReportContext vhReportContext, 
        IMemoryCache memoryCache,
        IOptions<AppOptions> appOptions)
        : base(logger, vhContext)
    {
        _vhReportContext = vhReportContext;
        _memoryCache = memoryCache;
        _appOptions = appOptions;
    }

    [HttpGet("{projectId:guid}")]
    public async Task<Project> Get(Guid projectId)
    {
        await VerifyUserPermission(VhContext, projectId, Permissions.ProjectRead);

        var query = VhContext.Projects
            .Include(x => x.ProjectRoles);

        var res = await query
            .SingleAsync(e => e.ProjectId == projectId);
        return res;
    }

    [HttpPost]
    public async Task<Project> Create(Guid? projectId = null)
    {
        projectId ??= Guid.NewGuid();

        var curUserId = await GetCurrentUserId(VhContext);
        await VerifyUserPermission(VhContext, curUserId, Permissions.ProjectCreate);

        // Check user quota
        using var singleRequest = SingleRequest.Start($"CreateProject_{curUserId}");

        // get user's maxProjects quota
        var user = await VhContext.Users.SingleAsync(x => x.UserId == curUserId);

        // find all user's project with owner role
        var query =
            from projectRole in VhContext.ProjectRoles
            join secureObjectRolePermission in VhContext.SecureObjectRolePermissions on projectRole.RoleId equals secureObjectRolePermission.RoleId
            join userRole in VhContext.RoleUsers on projectRole.RoleId equals userRole.RoleId
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
        var ownersRole = await VhContext.AuthManager.Role_Create(Resource.ProjectOwners, curUserId);
        var viewersRole = await VhContext.AuthManager.Role_Create(Resource.ProjectViewers, curUserId);

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

        await VhContext.Projects.AddAsync(project);

        // Grant permissions
        var secureObject = await VhContext.AuthManager.CreateSecureObject(projectId.Value, SecureObjectTypes.Project);
        await VhContext.AuthManager.SecureObject_AddRolePermission(secureObject, ownersRole, PermissionGroups.ProjectOwner, curUserId);
        await VhContext.AuthManager.SecureObject_AddRolePermission(secureObject, viewersRole, PermissionGroups.ProjectViewer, curUserId);

        // add current user as the admin
        await VhContext.AuthManager.Role_AddUser(ownersRole.RoleId, curUserId, curUserId);

        await VhContext.SaveChangesAsync();
        return project;
    }

    [HttpGet]
    public async Task<Project[]> List()
    {
        var curUserId = await GetCurrentUserId(VhContext);
        await VerifyUserPermission(VhContext, curUserId, Permissions.ProjectList);

        // no lock
        await using var trans = await VhContext.WithNoLockTransaction();

        var query =
            from project in VhContext.Projects
            join projectRole in VhContext.ProjectRoles on project.ProjectId equals projectRole.ProjectId
            join roleUser in VhContext.RoleUsers on projectRole.RoleId equals roleUser.RoleId
            where roleUser.UserId == curUserId
            select project;

        var ret = await query
            .Distinct()
            .ToArrayAsync();

        return ret;
    }

    [HttpGet("usage-live-summary")]
    public async Task<LiveUsageSummary> GeLiveUsageSummary(Guid projectId)
    {
        await VerifyUserPermission(VhContext, projectId, Permissions.ProjectRead);

        // no lock
        await using var trans = await VhContext.WithNoLockTransaction();

        var lostThresholdTime = DateTime.UtcNow.Subtract(_appOptions.Value.LostServerThreshold);
        var query =
            from server in VhContext.Servers
            join serverStatus in VhContext.ServerStatuses on
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

    [HttpGet("usage-summary")]
    public async Task<Usage> GetUsageSummary(Guid projectId, DateTime? startTime = null, DateTime? endTime = null)
    {
        if (startTime == null) throw new ArgumentNullException(nameof(startTime));
        await VerifyUserPermission(VhContext, projectId, Permissions.ProjectRead);

        // no lock
        await using var trans = await VhContext.WithNoLockTransaction();
        await using var transReport = await _vhReportContext.WithNoLockTransaction();

        // check cache
        var cacheKey = AccessUtil.GenerateCacheKey($"project_usage_{projectId}", startTime, endTime, out var cacheExpiration);
        if (cacheKey != null && _memoryCache.TryGetValue(cacheKey, out Usage cacheRes))
            return cacheRes;

        // select and order
        var query =
            from accessUsage in _vhReportContext.AccessUsages
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

    [HttpGet("usage-history")]
    public async Task<ServerUsage[]> GeUsageHistory(Guid projectId, DateTime? startTime, DateTime? endTime = null)
    {
        if (startTime == null) throw new ArgumentNullException(nameof(startTime));
        endTime ??= DateTime.UtcNow;
        await VerifyUserPermission(VhContext, projectId, Permissions.ProjectRead);

        // no lock
        await using var trans = await VhContext.WithNoLockTransaction();
        await using var transReport = await _vhReportContext.WithNoLockTransaction();

        // check cache
        var cacheKey = AccessUtil.GenerateCacheKey($"project_usage_history_{projectId}", startTime, endTime, out var cacheExpiration);
        if (cacheKey != null && _memoryCache.TryGetValue(cacheKey, out ServerUsage[] cacheRes))
            return cacheRes;

        // go back to the time that ensure all servers sent their status
        var serverUpdateStatusInterval = _appOptions.Value.ServerUpdateStatusInterval * 2;
        endTime = endTime.Value.Subtract(_appOptions.Value.ServerUpdateStatusInterval);
        var step1 = serverUpdateStatusInterval.TotalMinutes;
        var step2 = (int)Math.Max(step1, (endTime.Value - startTime.Value).TotalMinutes / 12 / step1);

        var baseTime = startTime.Value;

        // per server in status interval
        var serverStatuses = _vhReportContext.ServerStatuses
            .Where(x => x.ProjectId == projectId && x.CreatedTime >= startTime && x.CreatedTime <= endTime)
            .GroupBy(serverStatus => new
            {
                Minutes = (long)(EF.Functions.DateDiffMinute(baseTime, serverStatus.CreatedTime) / step1),
                serverStatus.ServerId
            })
            .Select(g => new
            {
                g.Key.Minutes,
                g.Key.ServerId,
                SessionCount = g.Max(x => x.SessionCount),
                TunnelTransferSpeed = g.Max(x => x.TunnelReceiveSpeed + x.TunnelSendSpeed),
            });

        // sum of max in status interval
        var serverStatuses2 = serverStatuses
            .GroupBy(x => x.Minutes)
            .Select(g => new
            {
                Minutes = g.Key,
                SessionCount = g.Sum(x => x.SessionCount),
                TunnelTransferSpeed = g.Sum(x => x.TunnelTransferSpeed),
                // ServerCount = g.Count() 
            });

        // scale down and find max
        var totalStatuses = serverStatuses2
            .GroupBy(x => (int)(x.Minutes / step2))
            .Select(g =>
                new ServerUsage
                {
                    Time = baseTime.AddMinutes(g.Key * step2 * step1),
                    SessionCount = g.Max(y => y.SessionCount),
                    TunnelTransferSpeed = g.Max(y => y.TunnelTransferSpeed),
                    // ServerCount = g.Max(y=>y.ServerCount) 
                })
            .OrderBy(x => x.Time);

        var res = await totalStatuses.ToListAsync();

        // add missed step
        var stepSize = step2 * step1;
        var stepCount = (int)((endTime - startTime).Value.TotalMinutes / stepSize) + 1;
        for (var i = 0; i < stepCount; i++)
        {
            var time = startTime.Value.AddMinutes(i * stepSize);
            if (res.Count <=i || res[i].Time!= time)
                res.Insert(i, new ServerUsage{Time = time});
        }

        // update cache
        if (cacheExpiration != null)
            _memoryCache.Set(cacheKey, res, cacheExpiration.Value);

        return res.ToArray();
    }
}