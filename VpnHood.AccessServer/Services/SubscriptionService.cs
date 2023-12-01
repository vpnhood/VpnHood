﻿using GrayMint.Authorization.RoleManagement.Abstractions;
using GrayMint.Authorization.UserManagement.Abstractions;
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
    private readonly IUserProvider _userProvider;
    private readonly IRoleProvider _roleProvider;

    private async Task<ProjectModel> GetProject(Guid projectId)
    {
        var project = await _vhContext.Projects.FindAsync(projectId);
        return project ?? throw new KeyNotFoundException($"Could not find project. ProjectId: {projectId}");
    }

    public SubscriptionService(
        VhContext vhContext,
        IRoleProvider roleProvider,
        IUserProvider userProvider)
    {
        _vhContext = vhContext;
        _roleProvider = roleProvider;
        _userProvider = userProvider;
    }

    public async Task AuthorizeCreateProject(string userId)
    {
        var user = await _userProvider.Get(userId); // make sure the user is registered
        var userRoles = await _roleProvider.GetUserRoles(new UserRoleCriteria { UserId = user.UserId });
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

    public async Task AuthorizeAddCertificate(Guid projectId)
    {
        if (await IsFreePlan(projectId) &&
            _vhContext.Certificates.Count(x => x.ProjectId == projectId && !x.IsDeleted) >= QuotaConstants.CertificateCount)
            throw new QuotaException(nameof(VhContext.Certificates), QuotaConstants.CertificateCount);
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

    public async Task VerifyAddUser(Guid projectId)
    {
        var userRoles = await _roleProvider.GetUserRoles(new UserRoleCriteria { ResourceId= projectId.ToString() });
        if (await IsFreePlan(projectId) && userRoles.Length > QuotaConstants.TeamUserCount)
            throw new QuotaException("TeamUserCount", QuotaConstants.TeamUserCount);
    }
}