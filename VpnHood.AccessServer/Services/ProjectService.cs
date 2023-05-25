using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using System.Linq;
using GrayMint.Authorization.RoleManagement.Abstractions;
using VpnHood.AccessServer.Controllers;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Security;
using VpnHood.Common.Utils;
using VpnHood.AccessServer.Persistence;
using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.Clients;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Report.Services;

namespace VpnHood.AccessServer.Services;

public class ProjectService
{
    private readonly VhContext _vhContext;
    private readonly AgentCacheClient _agentCacheClient;
    private readonly UsageReportService _usageReportService;
    private readonly SubscriptionService _subscriptionService;
    private readonly CertificateService _certificateService;
    private readonly IRoleProvider _roleProvider;

    public ProjectService(
        VhContext vhContext,
        SubscriptionService subscriptionService,
        AgentCacheClient agentCacheClient,
        UsageReportService usageReportService,
        IRoleProvider roleProvider, 
        CertificateService certificateService)
    {
        _subscriptionService = subscriptionService;
        _vhContext = vhContext;
        _agentCacheClient = agentCacheClient;
        _usageReportService = usageReportService;
        _roleProvider = roleProvider;
        _certificateService = certificateService;
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
            CreatedTime = DateTime.UtcNow,
        };

        // Farm
        var serverFarm = new ServerFarmModel
        {
            ServerFarmId = Guid.NewGuid(),
            ServerFarmName = "Server Farm 1",
            UseHostName = false,
            Certificate = await _certificateService.CreateSelfSingedInternal(projectId),
            ServerProfile = serverProfile,
            Secret = VhUtil.GenerateKey(),
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
                    Secret = VhUtil.GenerateKey(),
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
                    Secret = VhUtil.GenerateKey(),
                    IsEnabled= true,
                    IsDeleted = false,
                    CreatedTime= DateTime.UtcNow
                }
            }
        };

        await _vhContext.Projects.AddAsync(project);
        await _vhContext.SaveChangesAsync();

        // make current user the owner
        await _roleProvider.AddUser(project.ProjectId.ToString(), Roles.ProjectOwner.RoleId, ownerUserId);
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

    public async Task<IEnumerable<Project>> List(IEnumerable<Guid> projectIds)
    {
        await using var trans = await _vhContext.WithNoLockTransaction();
        var projects = await _vhContext.Projects
            .Where(x => projectIds.Contains(x.ProjectId))
            .OrderByDescending(x => x.ProjectName)
            .ToArrayAsync();

        return projects.Select(project => project.ToDto());
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