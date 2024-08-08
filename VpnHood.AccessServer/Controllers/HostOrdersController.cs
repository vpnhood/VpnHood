using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using VpnHood.AccessServer.Dtos.HostOrders;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.Services;

namespace VpnHood.AccessServer.Controllers;

[ApiController]
[Authorize]
[Route("/api/v{version:apiVersion}/projects/{projectId}/host-orders")]
public class HostOrdersController(HostOrdersService hostOrdersService)
{
    [HttpGet("ips")]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public Task<HostIp[]> ListIps(Guid projectId)
    {
        throw new NotImplementedException();
    }

    [HttpGet]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public Task<HostOrder[]> List(Guid projectId)
    {
        throw new NotImplementedException();
    }

    [HttpGet("{orderId}")]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public Task<HostOrder> Get(Guid projectId, string orderId)
    {
        throw new NotImplementedException();
    }

    [HttpPost("order-new-ip")]
    [AuthorizeProjectPermission(Permissions.ProjectWrite)]
    public Task<HostOrder> OrderNewIp(Guid projectId, Guid serverId)
    {
        return hostOrdersService.OrderNewIp(projectId, serverId);
    }

    [AuthorizeProjectPermission(Permissions.ProjectWrite)]
    [HttpDelete("order-release-ip")]
    public Task<string> OrderReleaseIp(Guid projectId, IPAddress ipAddress, bool ignoreProviderError)
    {
        throw new NotImplementedException();
    }

    [HttpPost("sync")]
    public Task Sync(Guid projectId)
    {
        throw new NotImplementedException();
    }

}