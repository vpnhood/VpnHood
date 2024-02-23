﻿using GrayMint.Authorization.RoleManagement.Abstractions;
using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.Clients;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Persistence.Enums;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.AccessServer.Persistence.Utils;
using VpnHood.AccessServer.Report.Services;
using VpnHood.AccessServer.Report.Views;
using VpnHood.AccessServer.Security;
using VpnHood.Common.Utils;

namespace VpnHood.AccessServer.Services;

public class ProjectService(
    VhContext vhContext,
    SubscriptionService subscriptionService,
    AgentCacheClient agentCacheClient,
    ReportUsageService usageReportService,
    IRoleProvider roleProvider)
{
    public async Task<Project> Create(string ownerUserId)
    {
        // Check user quota
        using var singleRequest = await AsyncLock.LockAsync($"{ownerUserId}_CreateProject");
        await subscriptionService.AuthorizeCreateProject(ownerUserId);
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
            ProjectId = projectId,
            ServerFarmId = Guid.NewGuid(),
            ServerFarmName = "Server Farm 1",
            UseHostName = false,
            ServerProfileId = serverProfile.ServerProfileId,
            ServerProfile = serverProfile,
            Secret = VhUtil.GenerateKey(),
            CreatedTime = DateTime.UtcNow,
            TokenJson = null,
            TokenUrl = null,
            PushTokenToClient = true,
            Servers = [],
        };
        FarmTokenBuilder.UpdateIfChanged(serverFarm);

        // create certificate
        var certificate = CertificateHelper.BuildSelfSinged(projectId, serverFarm.ServerFarmId, createParams: null);
        serverFarm.Certificates = [certificate];

        // create project
        var project = new ProjectModel
        {
            ProjectId = projectId,
            ProjectName = null,
            SubscriptionType = SubscriptionType.Free,
            CreatedTime = DateTime.UtcNow,
            GaApiSecret = null,
            GaMeasurementId = null,
            LetsEncryptAccount = null,
            ServerProfiles = new HashSet<ServerProfileModel>
            {
                serverProfile
            },
            ServerFarms = new HashSet<ServerFarmModel>
            {
                serverFarm
            },
            AccessTokens = new HashSet<AccessTokenModel>
            {
                new()
                {
                    ProjectId = projectId,
                    AccessTokenId = Guid.NewGuid(),
                    ServerFarm = serverFarm,
                    AccessTokenName = "Public",
                    SupportCode = 1000,
                    Secret = VhUtil.GenerateKey(),
                    IsPublic = true,
                    IsEnabled= true,
                    IsDeleted = false,
                    CreatedTime= DateTime.UtcNow,
                    ModifiedTime = DateTime.UtcNow,
                    ExpirationTime = null,
                    LastUsedTime = null,
                    MaxDevice = 0,
                    MaxTraffic = 0,
                    Lifetime = 0,
                    Url = null,
                    FirstUsedTime = null,
                    ServerFarmId = serverFarm.ServerFarmId
                },

                new()
                {
                    ProjectId = projectId,
                    AccessTokenId = Guid.NewGuid(),
                    ServerFarmId = serverFarm.ServerFarmId ,
                    ServerFarm = serverFarm,
                    AccessTokenName = "Private 1",
                    IsPublic = false,
                    SupportCode = 1001,
                    MaxDevice = 5,
                    Secret = VhUtil.GenerateKey(),
                    IsEnabled= true,
                    IsDeleted = false,
                    CreatedTime= DateTime.UtcNow,
                    ModifiedTime = DateTime.UtcNow,
                    FirstUsedTime = null,
                    ExpirationTime = null,
                    LastUsedTime = null,
                    MaxTraffic = 0,
                    Lifetime = 0,
                    Url = null
                }
            }
        };

        await vhContext.Projects.AddAsync(project);
        await vhContext.SaveChangesAsync();

        // make current user the owner
        await roleProvider.AddUserRole(project.ProjectId.ToString(), Roles.ProjectOwner.RoleId, ownerUserId);
        return project.ToDto();
    }

    public async Task<Project> Get(Guid projectId)
    {
        var project = await vhContext.Projects.SingleAsync(e => e.ProjectId == projectId);
        return project.ToDto();
    }

    public async Task<Project> Update(Guid projectId, ProjectUpdateParams updateParams)
    {
        var project = await vhContext.Projects.SingleAsync(e => e.ProjectId == projectId);

        if (updateParams.ProjectName != null) project.ProjectName = updateParams.ProjectName;
        if (updateParams.GaMeasurementId != null) project.GaMeasurementId = updateParams.GaMeasurementId;
        if (updateParams.GaApiSecret != null) project.GaApiSecret = updateParams.GaApiSecret;
        await vhContext.SaveChangesAsync();
        await agentCacheClient.InvalidateProject(projectId);

        return project.ToDto();
    }

    public async Task<Project[]> List(string? search = null, int recordIndex = 0, int recordCount = 101)
    {
        // no lock
        await using var trans = await vhContext.WithNoLockTransaction();
        var projects = await vhContext.Projects
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

    public async Task<IEnumerable<Project>> List(IEnumerable<Guid> projectIds)
    {
        await using var trans = await vhContext.WithNoLockTransaction();
        var projects = await vhContext.Projects
            .Where(x => projectIds.Contains(x.ProjectId))
            .OrderByDescending(x => x.ProjectName)
            .ToArrayAsync();

        return projects.Select(project => project.ToDto());
    }

    public async Task<Usage> GetUsage(Guid projectId, DateTime? usageBeginTime, DateTime? usageEndTime = null,
        Guid? serverFarmId = null, Guid? serverId = null)
    {
        if (usageBeginTime == null) throw new ArgumentNullException(nameof(usageBeginTime));
        await subscriptionService.VerifyUsageQueryPermission(projectId, usageBeginTime, usageEndTime);

        var usage = await usageReportService.GetUsage(projectId, usageBeginTime.Value, usageEndTime,
            serverFarmId: serverFarmId, serverId: serverId);
        return usage;
    }
}