using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VpnHood.AccessServer.Clients;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Exceptions;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.MultiLevelAuthorization.Services;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.ServerUtils;
using VpnHood.Common;

namespace VpnHood.AccessServer.Controllers;

[Route("/api/projects")]
public class ProjectController : SuperController<ProjectController>
{
    private readonly IMemoryCache _memoryCache;
    private readonly AppOptions _appOptions;
    private readonly VhReportContext _vhReportContext;
    private readonly AgentCacheClient _agentCacheClient;
    private readonly MultilevelAuthService _multilevelAuthService;

    public ProjectController(ILogger<ProjectController> logger,
        VhContext vhContext,
        VhReportContext vhReportContext,
        IMemoryCache memoryCache,
        IOptions<AppOptions> appOptions,
        AgentCacheClient agentCacheClient,
        MultilevelAuthService multilevelAuthService)
        : base(logger, vhContext, multilevelAuthService)
    {
        _vhReportContext = vhReportContext;
        _memoryCache = memoryCache;
        _appOptions = appOptions.Value;
        _agentCacheClient = agentCacheClient;
        _multilevelAuthService = multilevelAuthService;
    }

    [HttpGet("{projectId:guid}")]
    public async Task<Project> Get(Guid projectId)
    {
        await VerifyUserPermission(VhContext, projectId, Permissions.ProjectRead);

        var project = await VhContext.Projects.SingleAsync(e => e.ProjectId == projectId);
        return project;
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
        var userRoles = await
            _multilevelAuthService.GetUserRolesByPermissionGroup(user.UserId, PermissionGroups.ProjectOwner.PermissionGroupId);
        var userProjectOwnerCount = userRoles.Length;
        if (userProjectOwnerCount >= user.MaxProjectCount)
            throw new QuotaException(nameof(VhContext.Projects), user.MaxProjectCount);

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
            }
        };

        await VhContext.Projects.AddAsync(project);


        // Grant permissions
        var secureObject = await _multilevelAuthService.CreateSecureObject(projectId.Value, SecureObjectTypes.Project);

        // Create roles
        var ownersRole = await _multilevelAuthService.Role_Create(projectId.Value, Resource.ProjectOwners, curUserId);
        var viewersRole = await _multilevelAuthService.Role_Create(projectId.Value, Resource.ProjectViewers, curUserId);

        // SecureObject
        await _multilevelAuthService.SecureObject_AddRolePermission(secureObject, ownersRole, PermissionGroups.ProjectOwner, curUserId);
        await _multilevelAuthService.SecureObject_AddRolePermission(secureObject, viewersRole, PermissionGroups.ProjectViewer, curUserId);

        // add current user as the admin
        await _multilevelAuthService.Role_AddUser(ownersRole.RoleId, curUserId, curUserId);

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

        var roles = await _multilevelAuthService.GetUserRoles(curUserId);
        var projectIds = roles.Select(x => x.SecureObjectId).Distinct();
        var projects = await VhContext.Projects
            .Where(x => projectIds.Contains(x.ProjectId))
            .ToArrayAsync();

        return projects;
    }

    [HttpGet("usage-live-summary")]
    public async Task<LiveUsageSummary> GeLiveUsageSummary(Guid projectId)
    {
        await VerifyUserPermission(VhContext, projectId, Permissions.ProjectRead);

        // no lock
        await using var trans = await VhContext.WithNoLockTransaction();

        var query =
            from server in VhContext.Servers
            join serverStatus in VhContext.ServerStatuses on
                new { key1 = server.ServerId, key2 = true } equals new
                    { key1 = serverStatus.ServerId, key2 = serverStatus.IsLast } into g0
            from serverStatus in g0.DefaultIfEmpty()
            where server.ProjectId == projectId
            select new { Server = server, ServerStatusEx = serverStatus };

        // update model ServerStatusEx
        var models = await query.ToArrayAsync();
        foreach (var model in models)
            model.Server.ServerStatus = model.ServerStatusEx;

        // update status from cache
        var cachedServers = await _agentCacheClient.GetServers(projectId);
        var servers = models.Select(x=>ServerConverter.FromModel(x.Server, _appOptions.LostServerThreshold)).ToArray();
        ServerUtil.UpdateByCache(servers, cachedServers);

        // create usage summary
        var usageSummary = new LiveUsageSummary
        {
            TotalServerCount = servers.Length,
            NotInstalledServerCount = servers.Count(x => x.ServerStatus is null),
            ActiveServerCount = servers.Count(x => x.ServerState is ServerState.Active),
            IdleServerCount = servers.Count(x => x.ServerState is ServerState.Idle),
            LostServerCount = servers.Count(x => x.ServerState is ServerState.Lost),
            SessionCount = servers.Where(x => x.ServerState is ServerState.Active).Sum(x => x.ServerStatus!.SessionCount),
            TunnelSendSpeed = servers.Where(x => x.ServerState is ServerState.Active).Sum(x => x.ServerStatus!.TunnelSendSpeed),
            TunnelReceiveSpeed = servers.Where(x => x.ServerState == ServerState.Active).Sum(x => x.ServerStatus!.TunnelReceiveSpeed),
        };

        return usageSummary;
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
        var serverUpdateStatusInterval = _appOptions.ServerUpdateStatusInterval * 2;
        endTime = endTime.Value.Subtract(_appOptions.ServerUpdateStatusInterval);
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
            if (res.Count <= i || res[i].Time != time)
                res.Insert(i, new ServerUsage { Time = time });
        }

        // update cache
        if (cacheExpiration != null)
            _memoryCache.Set(cacheKey, res, cacheExpiration.Value);

        return res.ToArray();
    }
}