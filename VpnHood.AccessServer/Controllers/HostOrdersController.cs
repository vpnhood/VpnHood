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
        return hostOrdersService.ListIps(projectId, search: search, recordIndex: recordIndex, recordCount: recordCount);
    }

    [AuthorizeProjectPermission(Permissions.ProjectWrite)]
    [HttpDelete("ips/{ipAddress}")]
    public Task ReleaseIp(Guid projectId, string ipAddress, bool ignoreProviderError = false)
    {
        return hostOrdersService.ReleaseIp(projectId, IPAddress.Parse(ipAddress), ignoreProviderError);
    }

    [HttpPost("order-new-ip")]
    [AuthorizeProjectPermission(Permissions.ProjectWrite)]
    public Task<HostOrder> CreateNewIpOrder(Guid projectId, Guid serverId)
    {
        return hostOrdersService.CreateNewIpOrder(projectId, serverId);
    }

    [HttpGet]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public Task<HostOrder[]> List(Guid projectId, int recordIndex = 0, int recordCount = 200)
    {
        return hostOrdersService.List(projectId, recordIndex: recordIndex, recordCount: recordCount);
    }

    [HttpGet("{orderId}")]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public Task<HostOrder> Get(Guid projectId, string orderId)
    {
        return hostOrdersService.Get(projectId, orderId);
    }
}