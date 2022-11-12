using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VpnHood.AccessServer.Clients;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Exceptions;
using VpnHood.AccessServer.MultiLevelAuthorization.Services;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.ServerUtils;
using VpnHood.AccessServer.Services;
using VpnHood.Common;

namespace VpnHood.AccessServer.Controllers;

[Route("/api/projects")]
public class ProjectController : SuperController<ProjectController>
{
    private readonly AppOptions _appOptions;
    private readonly AgentCacheClient _agentCacheClient;
    private readonly MultilevelAuthService _multilevelAuthService;
    private readonly UsageReportService _usageReportService;

    public ProjectController(ILogger<ProjectController> logger,
        VhContext vhContext,
        IOptions<AppOptions> appOptions,
        AgentCacheClient agentCacheClient,
        MultilevelAuthService multilevelAuthService, 
        UsageReportService usageReportService)
        : base(logger, vhContext, multilevelAuthService)
    {
        _appOptions = appOptions.Value;
        _agentCacheClient = agentCacheClient;
        _multilevelAuthService = multilevelAuthService;
        _usageReportService = usageReportService;
    }

    [HttpPost]
    public async Task<Project> Create(Guid? projectId = null)
    {
        projectId ??= Guid.NewGuid();

        var curUserId = await GetCurrentUserId(VhContext);
        await VerifyUserPermission(curUserId, Permissions.ProjectCreate);

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
        var accessPointGroup = new Models.AccessPointGroup()
        {
            AccessPointGroupId = Guid.NewGuid(),
            AccessPointGroupName = "Group1",
            Certificate = CertificateController.CreateInternal(projectId.Value, null)
        };

        // create project
        var project = new Models.Project()
        {
            ProjectId = projectId.Value,
            SubscriptionType = Models.SubscriptionType.Free,
            AccessPointGroups = new HashSet<Models.AccessPointGroup>
            {
                accessPointGroup,
            },
            AccessTokens = new HashSet<Models.AccessToken>
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
        return project.ToDto();
    }

    [HttpGet("{projectId:guid}")]
    public async Task<Project> Get(Guid projectId)
    {
        await VerifyUserPermission(projectId, Permissions.ProjectRead);

        var project = await VhContext.Projects.SingleAsync(e => e.ProjectId == projectId);
        return project.ToDto();
    }

    [HttpPatch("{projectId:guid}")]
    public async Task<Project> Update(Guid projectId, ProjectUpdateParams updateParams)
    {
        await VerifyUserPermission(projectId, Permissions.ProjectWrite);
        var project = await VhContext.Projects.SingleAsync(e => e.ProjectId == projectId);

        if (updateParams.ProjectName != null) project.ProjectName = updateParams.ProjectName;
        if (updateParams.GoogleAnalyticsTrackId != null) project.GaTrackId = updateParams.GoogleAnalyticsTrackId;
        await _agentCacheClient.InvalidateProject(projectId);
        await VhContext.SaveChangesAsync();
        return project.ToDto();
    }

    [HttpGet]
    public async Task<Project[]> List()
    {
        var curUserId = await GetCurrentUserId(VhContext);
        await VerifyUserPermission(curUserId, Permissions.ProjectList);

        // no lock
        await using var trans = await VhContext.WithNoLockTransaction();

        var roles = await _multilevelAuthService.GetUserRoles(curUserId);
        var projectIds = roles.Select(x => x.SecureObjectId).Distinct();
        var projects = await VhContext.Projects
            .Where(x => projectIds.Contains(x.ProjectId))
            .ToArrayAsync();

        return projects.Select(project => project.ToDto()).ToArray();
    }

    [HttpGet("usage-live-summary")]
    public async Task<LiveUsageSummary> GetLiveUsageSummary(Guid projectId)
    {
        await VerifyUserPermission(projectId, Permissions.ProjectRead);

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

        var servers = models.Select(x => x.Server.ToDto(_appOptions.LostServerThreshold)).ToArray();
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
    public async Task<Usage> GetUsageSummary(Guid projectId, DateTime? usageStartTime, DateTime? usageEndTime = null)
    {
        if (usageStartTime == null) throw new ArgumentNullException(nameof(usageStartTime));
        await VerifyUserPermission(projectId, Permissions.ProjectRead);
        await VerifyUsageQueryPermission(projectId, usageStartTime, usageEndTime);

        var usage = await _usageReportService.GetUsageSummary(projectId, usageStartTime.Value, usageEndTime);
        return usage;
    }

    [HttpGet("usage-history")]
    public async Task<ServerUsage[]> GetUsageHistory(Guid projectId, DateTime? usageStartTime, DateTime? usageEndTime = null)
    {
        if (usageStartTime == null) throw new ArgumentNullException(nameof(usageStartTime));
        usageEndTime ??= DateTime.UtcNow;

        await VerifyUserPermission(projectId, Permissions.ProjectRead);
        await VerifyUsageQueryPermission(projectId, usageStartTime, usageEndTime);

        var serverUsages = await _usageReportService.GetUsageHistory(projectId, usageStartTime.Value, usageEndTime);
        return serverUsages;
    }
}