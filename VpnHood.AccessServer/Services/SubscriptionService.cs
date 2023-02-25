using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Exceptions;
using VpnHood.AccessServer.Persistence;

namespace VpnHood.AccessServer.Services;

public class SubscriptionService
{
    private readonly VhContext _vhContext;

    public SubscriptionService(VhContext vhContext)
    {
        _vhContext = vhContext;
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


    protected async Task<bool> IsFreePlan(Guid projectId)
    {
        var project = await _vhContext.Projects.SingleAsync(project => project.ProjectId == projectId);
        return project.SubscriptionType == SubscriptionType.Free;
    }
}