using GrayMint.Common.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VpnHood.AccessServer.Dtos.ClientFilters;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.Services;

namespace VpnHood.AccessServer.Controllers;

[ApiController]
[Authorize]
[Route("/api/v{version:apiVersion}/projects/{projectId}/accesses")]

public class ClientFiltersController(
    SubscriptionService subscriptionService,
    ClientFilterService clientFilterService)
{
    [HttpPost]
    [AuthorizeProjectPermission(Permissions.ProjectWrite)]
    public async Task<ClientFilter> Create(Guid projectId, ClientFilterCreate createParams)
    {
        // check user quota
        using var singleRequest = await AsyncLock.LockAsync($"{projectId}_CreateClientFilter");
        await subscriptionService.AuthorizeCreateClientFilter(projectId);
        return await clientFilterService.Create(projectId, createParams);
    }

}