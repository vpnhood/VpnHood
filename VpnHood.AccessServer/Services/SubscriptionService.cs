using GrayMint.Authorization.RoleManagement.Abstractions;
using GrayMint.Authorization.UserManagement.Abstractions;
using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.Exceptions;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Persistence.Enums;
using VpnHood.AccessServer.Repos;
using VpnHood.AccessServer.Security;

namespace VpnHood.AccessServer.Services;

public class SubscriptionService(
    VhContext vhContext,
    VhRepo vhRepo,
    IRoleAuthorizationProvider roleProvider,
    IUserProvider userProvider)
{
    public async Task AuthorizeCreateProject(string userId)
    {
        var user = await userProvider.Get(userId); // make sure the user is registered
        var userRoles = await roleProvider.GetUserRoles(new UserRoleCriteria { UserId = user.UserId });
        if (userRoles.Count(x => x.Role.RoleName == Roles.ProjectOwner.RoleName) >= QuotaConstants.ProjectCount)
            throw new QuotaException(nameof(vhContext.Projects), QuotaConstants.ProjectCount,
                $"You can not be owner of more than {QuotaConstants.ProjectCount} projects.");
    }

    public async Task AuthorizeCreateServerFarm(Guid projectId)
    {
        if (await IsFreePlan(projectId) &&
            (await vhRepo.ServerFarmNames(projectId)).Length >= QuotaConstants.ServerFarmCount)
            throw new QuotaException(nameof(VhContext.ServerFarms), QuotaConstants.ServerFarmCount);
    }

    public async Task AuthorizeCreateServer(Guid projectId)
    {
        if (await IsFreePlan(projectId) &&
            vhContext.Servers.Count(server => server.ProjectId == projectId && !server.IsDeleted) >=
            QuotaConstants.ServerCount)
            throw new QuotaException(nameof(VhContext.Servers), QuotaConstants.ServerCount);
    }

    public async Task AuthorizeCreateAccessToken(Guid projectId)
    {
        if (await IsFreePlan(projectId) &&
            vhContext.AccessTokens.Count(accessToken => accessToken.ProjectId == projectId && !accessToken.IsDeleted) >=
            QuotaConstants.AccessTokenCount)
            throw new QuotaException(nameof(VhContext.AccessTokens), QuotaConstants.AccessTokenCount);
    }

    public async Task AuthorizeAddCertificate(Guid projectId)
    {
        if (await IsFreePlan(projectId) &&
            vhContext.Certificates.Count(x => x.ProjectId == projectId && !x.IsDeleted) >=
            QuotaConstants.CertificateCount)
            throw new QuotaException(nameof(VhContext.Certificates), QuotaConstants.CertificateCount);
    }

    private async Task<bool> IsFreePlan(Guid projectId)
    {
        var project = await vhContext.Projects.SingleAsync(project => project.ProjectId == projectId);
        return project.SubscriptionType == SubscriptionType.Free;
    }

    public async Task VerifyUsageQueryPermission(Guid projectId, DateTime? usageBeginTime, DateTime? usageEndTime)
    {
        var project = await vhRepo.ProjectGet(projectId);
        usageBeginTime ??= DateTime.UtcNow;
        usageEndTime ??= DateTime.UtcNow;
        var requestTimeSpan = usageEndTime - usageBeginTime;
        var maxTimeSpan = project.SubscriptionType == SubscriptionType.Free
            ? QuotaConstants.UsageQueryTimeSpanFree
            : QuotaConstants.UsageQueryTimeSpanPremium;

        if (requestTimeSpan > maxTimeSpan)
            throw new QuotaException("UsageQuery", (long)maxTimeSpan.TotalHours,
                "The usage query period is not supported by your plan.");
    }
}