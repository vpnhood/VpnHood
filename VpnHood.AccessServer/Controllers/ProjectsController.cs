﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Clients;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Exceptions;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.MultiLevelAuthorization.Services;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.Services;
using VpnHood.Common;

namespace VpnHood.AccessServer.Controllers;

[Route("/api/v{version:apiVersion}/projects")]
public class ProjectsController : SuperController<ProjectsController>
{
    private readonly AgentCacheClient _agentCacheClient;
    private readonly MultilevelAuthService _multilevelAuthService;
    private readonly UsageReportService _usageReportService;

    public ProjectsController(ILogger<ProjectsController> logger,
        VhContext vhContext,
        AgentCacheClient agentCacheClient,
        MultilevelAuthService multilevelAuthService,
        UsageReportService usageReportService)
        : base(logger, vhContext, multilevelAuthService)
    {
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
        var accessPointGroup = new AccessPointGroupModel
        {
            AccessPointGroupId = Guid.NewGuid(),
            AccessPointGroupName = "Group1",
            Certificate = CertificatesController.CreateInternal(projectId.Value, null)
        };

        // create project
        var project = new ProjectModel
        {
            ProjectId = projectId.Value,
            SubscriptionType = SubscriptionType.Free,
            AccessPointGroups = new HashSet<AccessPointGroupModel>
            {
                accessPointGroup,
            },
            AccessTokens = new HashSet<AccessTokenModel>
            {
                new()
                {
                    AccessTokenId = Guid.NewGuid(),
                    AccessPointGroup = accessPointGroup,
                    AccessTokenName = "Public",
                    SupportCode = 1000,
                    Secret = Util.GenerateSessionKey(),
                    IsPublic = true,
                    IsEnabled= true
                },

                new()
                {
                    AccessTokenId = Guid.NewGuid(),
                    AccessPointGroup = accessPointGroup,
                    AccessTokenName = "Private 1",
                    IsPublic = false,
                    SupportCode = 1001,
                    MaxDevice = 5,
                    Secret = Util.GenerateSessionKey(),
                    IsEnabled= true
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

    [HttpGet("usage")]
    public async Task<Usage> GetUsage(Guid projectId, DateTime? usageStartTime, DateTime? usageEndTime = null,
        Guid? accessPointGroupId = null, Guid? serverId = null)
    {
        if (usageStartTime == null) throw new ArgumentNullException(nameof(usageStartTime));
        await VerifyUserPermission(projectId, Permissions.ProjectRead);
        await VerifyUsageQueryPermission(projectId, usageStartTime, usageEndTime);

        var usage = await _usageReportService.GetUsage(projectId, usageStartTime.Value, usageEndTime,
            accessPointGroupId: accessPointGroupId, serverId: serverId);
        return usage;
    }
}