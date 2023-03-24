﻿using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using System.Linq;
using VpnHood.AccessServer.Controllers;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Security;
using VpnHood.Common.Utils;
using VpnHood.AccessServer.Persistence;
using GrayMint.Common.AspNetCore.SimpleUserManagement;
using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.Clients;

namespace VpnHood.AccessServer.Services;

public class ProjectService
{
    private readonly VhContext _vhContext;
    private readonly AgentCacheClient _agentCacheClient;
    private readonly UsageReportService _usageReportService;
    private readonly SubscriptionService _subscriptionService;
    private readonly SimpleRoleProvider _simpleRoleProvider;

    public ProjectService(
        VhContext vhContext, 
        SubscriptionService subscriptionService, 
        AgentCacheClient agentCacheClient, 
        UsageReportService usageReportService, 
        SimpleRoleProvider simpleRoleProvider)
    {
        _subscriptionService = subscriptionService;
        _vhContext = vhContext;
        _agentCacheClient = agentCacheClient;
        _usageReportService = usageReportService;
        _simpleRoleProvider = simpleRoleProvider;
    }

    public async Task<Project> Create(Guid ownerUserId)
    {
        // Check user quota
        using var singleRequest = await AsyncLock.LockAsync($"{ownerUserId}_CreateProject");
        await _subscriptionService.AuthorizeCreateProject(ownerUserId);
        var projectId = Guid.NewGuid();

        // ServerProfile
        var serverProfile = new ServerProfileModel
        {
            ServerProfileId = Guid.NewGuid(),
            ServerProfileName = Resource.DefaultServerProfile,
            IsDefault = true,
            IsDeleted = false,
            CreatedTime = DateTime.UtcNow
        };

        // Farm
        var serverFarm = new ServerFarmModel
        {
            ServerFarmId = Guid.NewGuid(),
            ServerFarmName = "Server Farm 1",
            Certificate = CertificatesController.CreateInternal(projectId, null),
            ServerProfile = serverProfile,
            CreatedTime = DateTime.UtcNow

        };

        // create project
        var project = new ProjectModel
        {
            ProjectId = projectId,
            SubscriptionType = SubscriptionType.Free,
            CreatedTime = DateTime.UtcNow,
            ServerProfiles = new HashSet<ServerProfileModel>
            {
                serverProfile,
            },
            ServerFarms = new HashSet<ServerFarmModel>
            {
                serverFarm,
            },
            AccessTokens = new HashSet<AccessTokenModel>
            {
                new()
                {
                    AccessTokenId = Guid.NewGuid(),
                    ServerFarm = serverFarm,
                    AccessTokenName = "Public",
                    SupportCode = 1000,
                    Secret = VhUtil.GenerateSessionKey(),
                    IsPublic = true,
                    IsEnabled= true,
                    IsDeleted = false,
                    CreatedTime= DateTime.UtcNow
                },

                new()
                {
                    AccessTokenId = Guid.NewGuid(),
                    ServerFarm = serverFarm,
                    AccessTokenName = "Private 1",
                    IsPublic = false,
                    SupportCode = 1001,
                    MaxDevice = 5,
                    Secret = VhUtil.GenerateSessionKey(),
                    IsEnabled= true,
                    IsDeleted = false,
                    CreatedTime= DateTime.UtcNow
                }
            }
        };

        await _vhContext.Projects.AddAsync(project);
        await _vhContext.SaveChangesAsync();

        // make current user the owner
        var role = await _simpleRoleProvider.GetByName(Roles.ProjectOwner.RoleName);
        await _simpleRoleProvider.AddUser(role.RoleId, ownerUserId, project.ProjectId.ToString());
        return project.ToDto();
    }

    public async Task<Project> Get(Guid projectId)
    {
        var project = await _vhContext.Projects.SingleAsync(e => e.ProjectId == projectId);
        return project.ToDto();
    }

    public async Task<Project> Update(Guid projectId, ProjectUpdateParams updateParams)
    {
        var project = await _vhContext.Projects.SingleAsync(e => e.ProjectId == projectId);

        if (updateParams.ProjectName != null) project.ProjectName = updateParams.ProjectName;
        if (updateParams.GoogleAnalyticsTrackId != null) project.GaTrackId = updateParams.GoogleAnalyticsTrackId;
        await _vhContext.SaveChangesAsync();
        await _agentCacheClient.InvalidateProject(projectId);

        return project.ToDto();
    }

    public async Task<Project[]> List(string? search = null, int recordIndex = 0, int recordCount = 101)
    {
        // no lock
        await using var trans = await _vhContext.WithNoLockTransaction();
        var projects = await _vhContext.Projects
            .Where(x =>
                string.IsNullOrEmpty(search) ||
                x.ProjectName!.Contains(search) ||
                x.ProjectId.ToString() == search)
            .OrderByDescending(x => x.CreatedTime)
            .Skip(recordIndex)
            .Take(recordCount)
            .ToArrayAsync();

        return projects.Select(project => project.ToDto()).ToArray();
    }

    public async Task<Usage> GetUsage(Guid projectId, DateTime? usageBeginTime, DateTime? usageEndTime = null,
        Guid? serverFarmId = null, Guid? serverId = null)
    {
        if (usageBeginTime == null) throw new ArgumentNullException(nameof(usageBeginTime));
        await _subscriptionService.VerifyUsageQueryPermission(projectId, usageBeginTime, usageEndTime);

        var usage = await _usageReportService.GetUsage(projectId, usageBeginTime.Value, usageEndTime,
            serverFarmId: serverFarmId, serverId: serverId);
        return usage;
    }
}