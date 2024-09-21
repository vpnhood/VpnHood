using GrayMint.Common.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VpnHood.AccessServer.Dtos.ClientFilters;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.Services;

namespace VpnHood.AccessServer.Controllers;

[ApiController]
[Authorize]
[Route("/api/v{version:apiVersion}/projects/{projectId}/client-filters")]

public class ClientFiltersController(
    SubscriptionService subscriptionService,
    ClientFilterService clientFilterService)
{
    [HttpPost]
    [AuthorizeProjectPermission(Permissions.ProjectWrite)]
    public async Task<ClientFilter> Create(Guid projectId, ClientFilterCreateParams createParamsParams)
    {
        // check user quota
        using var singleRequest = await AsyncLock.LockAsync($"{projectId}_CreateClientFilter");
        await subscriptionService.AuthorizeCreateClientFilter(projectId);
        return await clientFilterService.Create(projectId, createParamsParams);
    }

    [HttpGet("{clientFilterId}")]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public async Task<ClientFilter> Get(Guid projectId, string clientFilterId)
    {
        return await clientFilterService.Get(projectId, int.Parse(clientFilterId));
    }

    [HttpPatch("{clientFilterId}")]
    [AuthorizeProjectPermission(Permissions.ProjectWrite)]
    public Task<ClientFilter> Update(Guid projectId, string clientFilterId, ClientFilterUpdateParams updateParams)
    {
        return clientFilterService.Update(projectId, int.Parse(clientFilterId), updateParams);
    }

    [HttpDelete("{clientFilterId}")]
    [AuthorizeProjectPermission(Permissions.ProjectWrite)]
    public Task Delete(Guid projectId, string clientFilterId)
    {
        return clientFilterService.Delete(projectId, int.Parse(clientFilterId));
    }

}