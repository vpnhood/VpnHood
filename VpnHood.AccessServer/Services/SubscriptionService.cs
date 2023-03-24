﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GrayMint.Common.AspNetCore.SimpleUserManagement;
using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Exceptions;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Security;

namespace VpnHood.AccessServer.Services;

public class SubscriptionService
{
    private readonly VhContext _vhContext;
    private readonly SimpleUserProvider _simpleUserProvider;
    private readonly SimpleRoleProvider _simpleRoleProvider;

    private async Task<ProjectModel> GetProject(Guid projectId)
    {
        var project = await _vhContext.Projects.FindAsync(projectId);
        return project ?? throw new KeyNotFoundException($"Could not find project. ProjectId: {projectId}");
    }

    public SubscriptionService(
        VhContext vhContext,
        SimpleRoleProvider simpleRoleProvider,
        SimpleUserProvider simpleUserProvider)
    {
        _vhContext = vhContext;
        _simpleRoleProvider = simpleRoleProvider;
        _simpleUserProvider = simpleUserProvider;
    }

    public async Task AuthorizeCreateProject(Guid userId)
    {
        var user = await _simpleUserProvider.Get(userId); // make sure the user is registered
        var userRoles = await _simpleRoleProvider.GetUserRoles(userId: user.UserId);
        if (userRoles.Count(x => x.Role.RoleName == Roles.ProjectOwner.RoleName) >= QuotaConstants.ProjectCount)
            throw new QuotaException(nameof(_vhContext.Projects), QuotaConstants.ProjectCount,
                $"You can not be owner of more than {QuotaConstants.ProjectCount} projects.");
    }

    public async Task AuthorizeCreateServer(Guid projectId)
    {
        if (await IsFreePlan(projectId) &&
            _vhContext.Servers.Count(server => server.ProjectId == projectId && !server.IsDeleted) >= QuotaConstants.ServerCount)
            throw new QuotaException(nameof(VhContext.Servers), QuotaConstants.ServerCount);
    }

    public async Task AuthorizeCreateAccessToken(Guid projectId)
    {
        if (await IsFreePlan(projectId) &&
            _vhContext.AccessTokens.Count(accessToken => accessToken.ProjectId == projectId && !accessToken.IsDeleted) >= QuotaConstants.AccessTokenCount)
            throw new QuotaException(nameof(VhContext.AccessTokens), QuotaConstants.AccessTokenCount);
    }

    private async Task<bool> IsFreePlan(Guid projectId)
    {
        var project = await _vhContext.Projects.SingleAsync(project => project.ProjectId == projectId);
        return project.SubscriptionType == SubscriptionType.Free;
    }

    public async Task VerifyUsageQueryPermission(Guid projectId, DateTime? usageBeginTime, DateTime? usageEndTime)
    {
        var project = await GetProject(projectId);
        usageBeginTime ??= DateTime.UtcNow;
        usageEndTime ??= DateTime.UtcNow;
        var requestTimeSpan = usageEndTime - usageBeginTime;
        var maxTimeSpan = project.SubscriptionType == SubscriptionType.Free
            ? QuotaConstants.UsageQueryTimeSpanFree
            : QuotaConstants.UsageQueryTimeSpanPremium;

        if (requestTimeSpan > maxTimeSpan)
            throw new QuotaException("UsageQuery", (long)maxTimeSpan.TotalHours, "The usage query period is not supported by your plan.");
    }
}