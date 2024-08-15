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
    public Task<HostIp[]> ListIps(Guid projectId, string? search = null, int recordIndex = 0, int recordCount = 200)
    {
        return hostOrdersService.ListIps(projectId, search: search, recordIndex: recordIndex, recordCount: recordIndex);
    }

    [HttpGet]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public Task<HostOrder[]> List(Guid projectId, int recordIndex = 0, int recordCount = 200)
    {
        return hostOrdersService.List(projectId, recordIndex: recordIndex, recordCount: recordIndex);
    }

    [HttpGet("{orderId}")]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public Task<HostOrder> Get(Guid projectId, string orderId)
    {
        return hostOrdersService.Get(projectId, orderId);
    }

    [HttpPost("order-new-ip")]
    [AuthorizeProjectPermission(Permissions.ProjectWrite)]
    public Task<HostOrder> OrderNewIp(Guid projectId, Guid serverId)
    {
        return hostOrdersService.OrderNewIp(projectId, serverId);
    }

    [AuthorizeProjectPermission(Permissions.ProjectWrite)]
    [HttpDelete("order-release-ip")]
    public Task<HostOrder> OrderReleaseIp(Guid projectId, string ipAddress, bool ignoreProviderError = false)
    {
        return hostOrdersService.OrderReleaseIp(projectId, IPAddress.Parse(ipAddress), ignoreProviderError);
    }

}